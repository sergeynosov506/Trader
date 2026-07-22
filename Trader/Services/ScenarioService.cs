using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Models;

namespace EconomicGame.Services
{
    /// <summary>
    /// Runs the "scenario / challenge" feature: applies starting conditions when a
    /// player picks a scenario, and evaluates win/loss each game tick.
    ///
    /// Scenarios are loaded from <see cref="appsettings.json"/> (section "Scenarios")
    /// at startup; see <see cref="Scenario"/> for the schema. The service itself
    /// is stateless apart from the loaded scenario list — all per-player state
    /// lives on <see cref="Player.ActiveScenarioId"/> / ScenarioStartTime / ScenarioStatus.
    /// </summary>
    public class ScenarioService
    {
        private IReadOnlyList<Scenario> _scenarios = new List<Scenario>();
        private Bank? _bank;

        /// <summary>
        /// Called by GameEngine after its own config is ready. We take a Bank
        /// handle here so StartScenario can set up the scenario's starting loan
        /// without pushing Bank into another layer.
        /// </summary>
        public void LoadScenarios(IReadOnlyList<Scenario> scenarios, Bank bank)
        {
            _scenarios = scenarios;
            _bank = bank;
        }

        public IReadOnlyList<Scenario> AllScenarios => _scenarios;

        /// <summary>
        /// True equity: NetWorth minus outstanding loan principal + penalties.
        /// Exposed so the UI can show the same number EvaluateScenario uses.
        /// </summary>
        public decimal ComputeEquity(Player player, IEnumerable<MarketItem>? marketItems = null)
        {
            var baseNetWorth = marketItems != null
                ? player.ComputeMarketNetWorth(marketItems)
                : player.NetWorth;
            var debt = player.Loans.Sum(l => l.Amount + l.Penalty);
            return baseNetWorth - debt;
        }

        public Scenario? GetScenario(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _scenarios.FirstOrDefault(s => s.Id == id);
        }

        public Scenario? GetActiveScenario(Player player)
        {
            return GetScenario(player.ActiveScenarioId);
        }

        /// <summary>
        /// Start a scenario for the player. Resets scenario tracking, applies
        /// starting money override, and takes the starting loan (if any).
        ///
        /// Does NOT wipe existing inventory / assets — that's a deliberate
        /// choice so players can try a scenario without losing their main save.
        /// UI warns the user to start on a fresh profile if they want a clean run.
        /// </summary>
        public bool StartScenario(Player player, string scenarioId, DateTime currentTime,
            IEnumerable<Player>? allPlayers = null, IEnumerable<MarketItem>? marketItems = null)
        {
            var scenario = GetScenario(scenarioId);
            if (scenario == null) return false;

            player.ActiveScenarioId = scenario.Id;
            player.ScenarioStartTime = currentTime;
            player.ScenarioStatus = ScenarioStatus.Active;

            if (scenario.StartingMoney.HasValue)
            {
                player.Money = scenario.StartingMoney.Value;
            }

            if (scenario.StartingLoan > 0 && _bank != null)
            {
                _bank.TakeLoan(player, scenario.StartingLoan, scenario.StartingLoanMonths, currentTime);
            }

            // --- Bot race setup (bot-competitors feature) ---
            // For wealth-race goals, 5 random bots become rivals. A rival wins the race
            // by GAINING as much equity as the player still needs — relative progress,
            // so an already-rich bot doesn't win at tick one.
            player.ScenarioRivalIds.Clear();
            player.ScenarioRivalStartEquity.Clear();
            player.ScenarioRaceWinner = null;
            player.ScenarioStartEquity = ComputeEquity(player, marketItems);

            bool raceGoal = scenario.Goal.Type is ScenarioGoalType.ReachNetWorth or ScenarioGoalType.ReachMoney;
            if (raceGoal && allPlayers != null)
            {
                var rivals = allPlayers
                    .Where(p => p.IsAI && !p.IsBankrupt && p.OwnerId == null)
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(5)
                    .ToList();
                foreach (var rival in rivals)
                {
                    player.ScenarioRivalIds.Add(rival.Id);
                    player.ScenarioRivalStartEquity[rival.Id] = ComputeEquity(rival, marketItems);
                }
            }

            return true;
        }

        /// <summary>
        /// Abandon an active scenario without resolving win/loss.
        /// </summary>
        public void AbandonScenario(Player player)
        {
            player.ActiveScenarioId = null;
            player.ScenarioStartTime = null;
            player.ScenarioStatus = ScenarioStatus.None;
            player.ScenarioRivalIds.Clear();
            player.ScenarioRivalStartEquity.Clear();
            player.ScenarioRaceWinner = null;
        }

        /// <summary>
        /// Game-days elapsed since the scenario started. Uses the same
        /// "day length" convention the rest of the game uses (real time advanced
        /// at 15 game-minutes per tick, so 1 game-day = 96 ticks).
        /// </summary>
        public double ElapsedDays(Player player, DateTime currentTime)
        {
            if (!player.ScenarioStartTime.HasValue) return 0;
            return (currentTime - player.ScenarioStartTime.Value).TotalDays;
        }

        /// <summary>
        /// Game-days remaining until the scenario expires. Never negative.
        /// </summary>
        public double RemainingDays(Player player, DateTime currentTime)
        {
            var s = GetActiveScenario(player);
            if (s == null) return 0;
            return Math.Max(0, s.TimeLimitDays - ElapsedDays(player, currentTime));
        }

        /// <summary>
        /// Evaluate the player's active scenario. Called once per game tick
        /// by GameEngine. Transitions status to Won / Lost when appropriate.
        ///
        /// Returns the status *after* evaluation so the caller can fire off
        /// UI notifications on the transition.
        /// </summary>
        public ScenarioStatus EvaluateScenario(Player player, DateTime currentTime,
            IEnumerable<MarketItem>? marketItems = null, IEnumerable<Player>? allPlayers = null)
        {
            if (player.ScenarioStatus != ScenarioStatus.Active)
                return player.ScenarioStatus;

            var scenario = GetActiveScenario(player);
            if (scenario == null)
            {
                player.ScenarioStatus = ScenarioStatus.None;
                return ScenarioStatus.None;
            }

            // --- Bot race check: did a rival make the required equity gain first? ---
            if (player.ScenarioRivalIds.Any() && allPlayers != null &&
                scenario.Goal.Type is ScenarioGoalType.ReachNetWorth or ScenarioGoalType.ReachMoney)
            {
                decimal requiredGain = scenario.Goal.Threshold - player.ScenarioStartEquity;
                if (requiredGain > 0)
                {
                    foreach (var rival in allPlayers.Where(p => player.ScenarioRivalIds.Contains(p.Id)))
                    {
                        var startEq = player.ScenarioRivalStartEquity.GetValueOrDefault(rival.Id);
                        var gain = ComputeEquity(rival, marketItems) - startEq;
                        if (gain >= requiredGain)
                        {
                            player.ScenarioRaceWinner = rival.Name;
                            player.ScenarioStatus = ScenarioStatus.Lost;
                            return ScenarioStatus.Lost;
                        }
                    }
                }
            }

            var elapsed = ElapsedDays(player, currentTime);
            // Use true equity (assets - outstanding loan principal + penalties) so
            // a scenario that hands the player a loan at t=0 doesn't count that
            // loan as positive net worth.
            var baseNetWorth = marketItems != null
                ? player.ComputeMarketNetWorth(marketItems)
                : player.NetWorth;
            var outstandingDebt = player.Loans
                .Where(l => !l.IsDefaulted || l.Amount > 0)
                .Sum(l => l.Amount + l.Penalty);
            var netWorth = baseNetWorth - outstandingDebt;

            switch (scenario.Goal.Type)
            {
                case ScenarioGoalType.ReachNetWorth:
                    if (netWorth >= scenario.Goal.Threshold)
                    {
                        player.ScenarioStatus = ScenarioStatus.Won;
                        return ScenarioStatus.Won;
                    }
                    if (elapsed >= scenario.TimeLimitDays)
                    {
                        player.ScenarioStatus = ScenarioStatus.Lost;
                        return ScenarioStatus.Lost;
                    }
                    break;

                case ScenarioGoalType.ReachMoney:
                    if (player.Money >= scenario.Goal.Threshold)
                    {
                        player.ScenarioStatus = ScenarioStatus.Won;
                        return ScenarioStatus.Won;
                    }
                    if (elapsed >= scenario.TimeLimitDays)
                    {
                        player.ScenarioStatus = ScenarioStatus.Lost;
                        return ScenarioStatus.Lost;
                    }
                    break;

                case ScenarioGoalType.ReachItemStock:
                    {
                        var targetItem = scenario.Goal.ItemName;
                        var currentQty = player.Inventory
                            .Where(i => i.ItemName.Equals(targetItem, StringComparison.OrdinalIgnoreCase))
                            .Sum(i => i.Quantity);

                        if (currentQty >= scenario.Goal.Threshold)
                        {
                            player.ScenarioStatus = ScenarioStatus.Won;
                            return ScenarioStatus.Won;
                        }
                        if (elapsed >= scenario.TimeLimitDays)
                        {
                            player.ScenarioStatus = ScenarioStatus.Lost;
                            return ScenarioStatus.Lost;
                        }
                    }
                    break;

                case ScenarioGoalType.SurviveWithNetWorthAbove:
                    // Fail early if you drop below the threshold (e.g. net worth goes negative)
                    if (netWorth < scenario.Goal.Threshold)
                    {
                        player.ScenarioStatus = ScenarioStatus.Lost;
                        return ScenarioStatus.Lost;
                    }
                    // Otherwise, winning condition is "still standing when time is up"
                    if (elapsed >= scenario.TimeLimitDays)
                    {
                        player.ScenarioStatus = ScenarioStatus.Won;
                        return ScenarioStatus.Won;
                    }
                    break;
            }

            return ScenarioStatus.Active;
        }
    }
}

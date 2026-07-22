using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Configuration;
using EconomicGame.Models;
using EconomicGame.Models.Poker;

namespace EconomicGame.Services
{
    public enum PokerTableTier { BackRoom, Club, HighRollers }

    /// <summary>
    /// Orchestrates poker sessions between the human player and X-Node bots.
    /// KEY DESIGN: money is REAL. A bot's table stack is escrowed from its actual
    /// Money; whatever you win at the table really leaves the bot's balance,
    /// and your losses really feed the bots. The bar's house games can't do that —
    /// this is the intended endgame gambling loop with a natural winnings cap:
    /// you can only win what the players at the table actually have.
    /// </summary>
    public class PokerService
    {
        private readonly GameEngine _gameEngine;
        private readonly PlayerService _playerService;
        private static readonly Random _rng = Random.Shared;

        public PokerService(GameEngine gameEngine, PlayerService playerService)
        {
            _gameEngine = gameEngine;
            _playerService = playerService;
        }

        // ---- Table tier configs ----
        public static (string Name, string Emoji, decimal SmallBlind, decimal BigBlind, decimal MinBuyIn, decimal MaxBuyIn) TierInfo(PokerTableTier tier) => tier switch
        {
            PokerTableTier.BackRoom => ("Задняя комната", "🚬", 10m, 20m, 500m, 2000m),
            PokerTableTier.Club => ("Покерный клуб", "🎩", 100m, 200m, 5000m, 25000m),
            _ => ("Хайроллеры", "💎", 1000m, 2000m, 50000m, 200000m)
        };

        /// <summary>Null = can join; otherwise a user-facing reason.</summary>
        public string? CanJoin(Player player, PokerTableTier tier)
        {
            if (player == null) return "Игрок не найден!";
            if (!_gameEngine.IsBarOpen) return _gameEngine.BarClosedMessage;
            if (SessionActive) return "Ты уже сидишь за столом!";

            var info = TierInfo(tier);
            switch (tier)
            {
                case PokerTableTier.Club:
                    if (player.Reputation < 60 && player.TradingLicenseLevel < 1)
                        return "В клуб пускают только уважаемых людей: нужна репутация 60+ или торговая лицензия.";
                    break;
                case PokerTableTier.HighRollers:
                    if (player.NetWorth < 500000m)
                        return "За стол хайроллеров садятся с капиталом от $500,000.";
                    break;
            }

            if (player.Money < info.MinBuyIn)
                return $"Минимальный бай-ин: {info.MinBuyIn:C}";

            if (FindEligibleBots(tier).Count < 2)
                return "Сейчас за столом нет достойных оппонентов. Загляни позже.";

            return null;
        }

        // ---- Session state ----
        public PokerTableTier Tier { get; private set; }
        public List<PokerSeat> Seats { get; } = new();
        public PokerHand? Hand { get; private set; }
        public bool SessionActive => Seats.Count > 0;
        public bool HandInProgress => Hand != null && !Hand.IsOver;
        public List<string> TableLog { get; } = new();
        public event Action? OnTableChanged;

        private readonly Dictionary<Guid, Player> _botsBySeat = new();
        private readonly Dictionary<Guid, decimal> _sessionProfitBySeat = new();
        private int _dealerIndex;
        private Guid _humanId;

        private void Log(string msg)
        {
            TableLog.Insert(0, msg);
            if (TableLog.Count > 30) TableLog.RemoveAt(TableLog.Count - 1);
        }

        private List<Player> FindEligibleBots(PokerTableTier tier)
        {
            var info = TierInfo(tier);
            // Bots need enough real money for a meaningful buy-in
            return _playerService.GetAllPlayers()
                .Where(p => p.IsAI && p.Money >= info.MinBuyIn * 2)
                .ToList();
        }

        public string JoinTable(Player player, PokerTableTier tier, decimal buyIn)
        {
            var reason = CanJoin(player, tier);
            if (reason != null) return reason;

            var info = TierInfo(tier);
            buyIn = Math.Clamp(buyIn, info.MinBuyIn, Math.Min(info.MaxBuyIn, player.Money));

            Tier = tier;
            Seats.Clear();
            _botsBySeat.Clear();
            _sessionProfitBySeat.Clear();
            TableLog.Clear();
            Hand = null;
            _dealerIndex = _rng.Next(0, 100);
            _humanId = player.Id;

            // Human seat (escrow real money into the table stack)
            player.Money -= buyIn;
            Seats.Add(new PokerSeat { PlayerId = player.Id, Name = player.Name, IsHuman = true, Stack = buyIn });
            _sessionProfitBySeat[player.Id] = -buyIn;

            // 3-4 bot opponents, richer bots at higher tiers
            var candidates = FindEligibleBots(tier).OrderByDescending(b => b.Money).Take(20).ToList();
            int botCount = Math.Min(candidates.Count, _rng.Next(3, 5));
            foreach (var bot in candidates.OrderBy(_ => _rng.Next()).Take(botCount))
            {
                decimal botBuyIn = Math.Clamp(
                    Math.Round(bot.Money * 0.10m),
                    info.MinBuyIn,
                    Math.Min(info.MaxBuyIn, bot.Money));
                bot.Money -= botBuyIn;
                Seats.Add(new PokerSeat { PlayerId = bot.Id, Name = bot.Name, IsHuman = false, Stack = botBuyIn });
                _botsBySeat[bot.Id] = bot;
                _sessionProfitBySeat[bot.Id] = -botBuyIn;
            }

            Log($"🪑 {player.Name} сел за стол «{info.Name}» с {buyIn:C}. Оппонентов: {botCount}.");
            _gameEngine.LogActivity($"{player.Name} сел за покерный стол «{info.Name}» ({buyIn:C})");
            OnTableChanged?.Invoke();
            return $"Ты за столом «{info.Name}». Блайнды {info.SmallBlind:C}/{info.BigBlind:C}. Удачи!";
        }

        public string StartHand()
        {
            if (!SessionActive) return "Ты не за столом!";
            if (HandInProgress) return "Раздача уже идёт!";
            if (!_gameEngine.IsBarOpen) return "Дилер накрывает стол тканью: «Бар закрывается, господа. Доиграем вечером — фишки никуда не денутся». Можешь встать и забрать свои.";

            // Bust bots leave the table (their zero stack means their escrow is gone)
            foreach (var seat in Seats.Where(s => !s.IsHuman && s.Stack <= 0).ToList())
            {
                Log($"💸 {seat.Name} проиграл всё и покидает стол.");
                Seats.Remove(seat);
                _botsBySeat.Remove(seat.PlayerId);
            }

            var human = Seats.FirstOrDefault(s => s.IsHuman);
            if (human == null || human.Stack <= 0)
                return "У тебя не осталось фишек. Покинь стол или перекупись, сев заново.";

            if (Seats.Count < 2)
            {
                Log("За столом не осталось оппонентов.");
                return "Оппоненты разошлись. Стол закрывается — забирай фишки.";
            }

            var info = TierInfo(Tier);
            _dealerIndex = (_dealerIndex + 1) % Seats.Count;
            Hand = new PokerHand(Seats, _dealerIndex, info.SmallBlind, info.BigBlind, _rng);
            _gameEngine.PokerHandInProgress = true;
            Log($"🃏 Новая раздача. Дилер: {Seats[Hand.DealerIndex].Name}.");

            AdvanceBots();
            OnTableChanged?.Invoke();
            return "Карты розданы!";
        }

        /// <summary>
        /// Let bots act until it's the human's turn or the hand ends.
        /// Bots decide via PokerBotBrain with their own genes and intoxication.
        /// </summary>
        public void AdvanceBots()
        {
            if (Hand == null) return;

            int guard = 0;
            while (!Hand.IsOver && guard++ < 200)
            {
                var seat = Hand.WhoseTurn;
                if (seat == null) break;
                if (seat.IsHuman) break;

                var bot = _botsBySeat.GetValueOrDefault(seat.PlayerId);
                var strat = bot?.Strategy ?? new GeneticStrategy();
                int drunk = bot?.IntoxicationLevel ?? 0;

                var decision = PokerBotBrain.Decide(Hand, seat, strat, drunk, _rng);
                var record = Hand.Apply(decision.Action, decision.BetTo);
                Log(FormatAction(record));
            }

            if (Hand.IsOver) SettleHand();
        }

        private string FormatAction(PokerActionRecord r) => r.Action switch
        {
            PokerActionType.Fold => $"🙅 {r.Name} сбросил карты",
            PokerActionType.Check => $"✋ {r.Name} чек",
            PokerActionType.Call => $"📞 {r.Name} колл {r.Amount:C}",
            PokerActionType.Bet => $"💰 {r.Name} ставит {r.Amount:C}",
            PokerActionType.Raise => $"⬆️ {r.Name} рейз (+{r.Amount:C})",
            _ => $"🔥 {r.Name} ОЛЛ-ИН ({r.Amount:C})!"
        };

        // ---- Human actions ----
        public string PlayerAct(PokerActionType action, decimal betTo = 0)
        {
            if (Hand == null || Hand.IsOver) return "Раздача не идёт!";
            var seat = Hand.WhoseTurn;
            if (seat == null || !seat.IsHuman) return "Сейчас не твой ход!";

            try
            {
                var record = Hand.Apply(action, betTo);
                Log(FormatAction(record));
            }
            catch (InvalidOperationException ex)
            {
                return $"Нельзя: {ex.Message}";
            }

            AdvanceBots();
            OnTableChanged?.Invoke();
            return "";
        }

        /// <summary>
        /// Buy a drink for a bot at the table — a perfectly legal tactic:
        /// drunk bots misread their hands. Uses the same intoxication system as the bar.
        /// </summary>
        public string BuyDrinkForBot(Player player, Guid botSeatId)
        {
            if (!SessionActive) return "Ты не за столом!";
            var bot = _botsBySeat.GetValueOrDefault(botSeatId);
            if (bot == null) return "Этот игрок не пьёт.";
            if (player.Money < GameConstants.WhiskeyPrice) return $"Не хватает денег ({GameConstants.WhiskeyPrice:C})";
            if (bot.IntoxicationLevel >= 4) return $"{bot.Name} уже в хлам — бармен отказывается наливать.";

            player.Money -= GameConstants.WhiskeyPrice;
            bot.IntoxicationLevel++;
            bot.SoberUpTime = _gameEngine.CurrentTime.AddMinutes(GameConstants.SoberUpMinutes * bot.IntoxicationLevel);
            Log($"🥃 {player.Name} угостил {bot.Name} виски. Тот довольно щурится...");
            OnTableChanged?.Invoke();
            return $"{bot.Name} выпил и, кажется, расслабился.";
        }

        public int GetBotIntoxication(Guid seatPlayerId) =>
            _botsBySeat.GetValueOrDefault(seatPlayerId)?.IntoxicationLevel ?? 0;

        private void SettleHand()
        {
            if (Hand == null) return;
            _gameEngine.PokerHandInProgress = false;

            var human = _playerService.GetCurrentPlayer();
            decimal potTotal = Hand.Results.Sum(r => r.AmountWon);

            foreach (var result in Hand.Results)
            {
                string catText = result.WonWithoutShowdown
                    ? "все сбросили"
                    : HandEvaluator.CategoryNameRu(result.Category ?? HandCategory.HighCard);
                Log($"🏆 {result.Name} забирает {result.AmountWon:C} ({catText})");
            }

            // ---- Stats + emotional consequences ----
            foreach (var seat in Seats)
            {
                var actor = seat.IsHuman ? human : _botsBySeat.GetValueOrDefault(seat.PlayerId);
                if (actor == null) continue;

                actor.PokerHandsPlayed++;
                var win = Hand.Results.FirstOrDefault(r => r.PlayerId == seat.PlayerId);
                decimal committed = seat.TotalCommitted;
                decimal net = (win?.AmountWon ?? 0m) - committed;
                actor.PokerProfit += net;

                if (win != null)
                {
                    actor.PokerHandsWon++;
                    if (win.AmountWon > actor.PokerBiggestPot) actor.PokerBiggestPot = win.AmountWon;
                }

                // Tilt: a bot that lost a big chunk gets angry and sloppy (Sentiment down),
                // and slightly more aggressive toward the player next time.
                if (!seat.IsHuman && net < 0 && committed > 0)
                {
                    var bot = _botsBySeat.GetValueOrDefault(seat.PlayerId);
                    if (bot != null && potTotal > 0 && -net > (seat.Stack + committed) * 0.25m)
                    {
                        bot.Strategy.Sentiment = Math.Max(-1.0, bot.Strategy.Sentiment - 0.2);
                        var humanWon = Hand.Results.Any(r => r.PlayerId == _humanId);
                        if (humanWon)
                        {
                            bot.Strategy.AggressionLevel = Math.Min(1.0, bot.Strategy.AggressionLevel + 0.05);
                            Log($"😠 {bot.Name} мрачно смотрит на тебя через стол...");
                        }
                    }
                }
            }

            // Big pots make the news
            if (human != null)
            {
                var humanResult = Hand.Results.FirstOrDefault(r => r.PlayerId == _humanId);
                if (humanResult != null && humanResult.AmountWon >= TierInfo(Tier).BigBlind * 25)
                {
                    _gameEngine.LogActivity($"🃏 {human.Name} сорвал банк {humanResult.AmountWon:C} за покерным столом!");
                }
            }

            OnTableChanged?.Invoke();
        }

        public string LeaveTable(Player player)
        {
            if (!SessionActive) return "Ты не за столом!";
            if (HandInProgress) return "Нельзя встать посреди раздачи! Доиграй руку.";

            decimal humanCashOut = 0;

            // Return every stack to its real owner
            foreach (var seat in Seats)
            {
                if (seat.IsHuman)
                {
                    player.Money += seat.Stack;
                    humanCashOut = seat.Stack;
                    _sessionProfitBySeat[seat.PlayerId] = _sessionProfitBySeat.GetValueOrDefault(seat.PlayerId) + seat.Stack;
                }
                else
                {
                    var bot = _botsBySeat.GetValueOrDefault(seat.PlayerId);
                    if (bot != null) bot.Money += seat.Stack;
                }
            }

            decimal sessionNet = _sessionProfitBySeat.GetValueOrDefault(player.Id);
            var summary = sessionNet >= 0
                ? $"Ты встал из-за стола с {humanCashOut:C} (+{sessionNet:C} за сессию) 🎉"
                : $"Ты встал из-за стола с {humanCashOut:C} ({sessionNet:C} за сессию) 😔";

            _gameEngine.LogActivity($"{player.Name} закончил покерную сессию: {(sessionNet >= 0 ? "+" : "")}{sessionNet:C}");

            Seats.Clear();
            _botsBySeat.Clear();
            _sessionProfitBySeat.Clear();
            Hand = null;
            _gameEngine.PokerHandInProgress = false;
            OnTableChanged?.Invoke();
            return summary;
        }

        /// <summary>
        /// Top poker players (human + bots) by lifetime poker profit — the bar's wall of fame.
        /// </summary>
        private List<(string Name, bool IsAI, int Hands, int Wins, decimal Profit, decimal BiggestPot)> _leaderboardCache = new();

        public List<(string Name, bool IsAI, int Hands, int Wins, decimal Profit, decimal BiggestPot)> GetLeaderboard(int top = 10)
        {
            try
            {
                _leaderboardCache = _playerService.GetAllPlayers()
                    .Where(p => p.PokerHandsPlayed > 0)
                    .OrderByDescending(p => p.PokerProfit)
                    .Take(top)
                    .Select(p => (p.Name, p.IsAI, p.PokerHandsPlayed, p.PokerHandsWon, p.PokerProfit, p.PokerBiggestPot))
                    .ToList();
            }
            catch (InvalidOperationException)
            {
                // Player list mutated during enumeration — serve the cached snapshot
            }
            return _leaderboardCache;
        }
    }
}

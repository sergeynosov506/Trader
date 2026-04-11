using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Models;
using EconomicGame.Configuration;

namespace EconomicGame.Services
{
    public class CorporateRivalryService
    {
        private readonly PlayerService _playerService;
        private readonly Random _random = Random.Shared;

        // Coalition tracking
        public List<Guid> CoalitionMembers { get; set; } = new List<Guid>();
        public int CurrentEscalationLevel { get; private set; } = 0;

        public CorporateRivalryService(PlayerService playerService)
        {
            _playerService = playerService;
        }

        /// <summary>
        /// Calculate escalation level based on player's total net worth.
        /// Level 0: < 50k (Neutral)
        /// Level 1: 50k-200k (Competitive)
        /// Level 2: 200k-500k (Aggressive)  
        /// Level 3: > 500k (Hostile)
        /// </summary>
        public int CalculateEscalationLevel(Player player)
        {
            var netWorth = player.NetWorth;
            if (netWorth >= GameConstants.EscalationLevel3Threshold) return 3;
            if (netWorth >= GameConstants.EscalationLevel2Threshold) return 2;
            if (netWorth >= GameConstants.EscalationLevel1Threshold) return 1;
            return 0;
        }

        public void UpdateRivals()
        {
            var userPlayer = _playerService.GetCurrentPlayer();
            if (userPlayer == null) return;

            CurrentEscalationLevel = CalculateEscalationLevel(userPlayer);

            var aiPlayers = _playerService.GetAllPlayers().Where(p => p.IsAI).ToList();
            if (!aiPlayers.Any()) return;

            // At level 0, no rivalry
            if (CurrentEscalationLevel == 0)
            {
                userPlayer.RivalPlayerIds.Clear();
                userPlayer.CorporateThreatLevel = 0.0m;
                CoalitionMembers.Clear();
                return;
            }

            // What the user is producing or holding in large quantities
            var userDominantItems = userPlayer.Inventory
                .Where(i => i.Quantity > 100)
                .Select(i => i.ItemName)
                .ToList();

            // Include stock holdings as "interests"
            var userStockInterests = userPlayer.Portfolio.Holdings
                .Where(h => h.Value > 50)
                .Select(h => h.Key)
                .ToList();

            // Find AI nodes that compete in these same items
            var potentialRivals = aiPlayers
                .Select(ai => new
                {
                    AI = ai,
                    OverlapScore = ai.Inventory.Count(i => userDominantItems.Contains(i.ItemName)),
                    WealthFactor = ai.Money / 50000m,
                    AggressionFit = ai.Strategy.AggressionLevel
                })
                .Where(x => x.OverlapScore > 0 || x.AggressionFit > 0.5)
                .OrderByDescending(x => x.OverlapScore * (decimal)x.WealthFactor * (decimal)x.AggressionFit)
                .Take(3 + CurrentEscalationLevel) // More rivals at higher levels
                .Select(x => x.AI.Id)
                .ToList();

            userPlayer.RivalPlayerIds = potentialRivals;

            // Calculate Threat Level
            var avgAiWealth = aiPlayers.Average(p => p.Money);
            userPlayer.CorporateThreatLevel = Math.Clamp(
                (decimal)CurrentEscalationLevel / 3.0m + (userPlayer.NetWorth / (avgAiWealth * 10)),
                0.1m, 1.0m);

            // Boost AI aggression based on escalation
            foreach (var rivalId in potentialRivals)
            {
                var rival = aiPlayers.FirstOrDefault(p => p.Id == rivalId);
                if (rival != null)
                {
                    // Escalation directly raises aggression genes
                    rival.Strategy.AggressionLevel = Math.Clamp(
                        rival.Strategy.AggressionLevel + CurrentEscalationLevel * 0.1, 0.0, 1.0);
                }
            }

            // Coalition Formation (Level 2+)
            if (CurrentEscalationLevel >= 2)
            {
                FormCoalition(aiPlayers);
            }
            else
            {
                CoalitionMembers.Clear();
            }
        }

        /// <summary>
        /// Forms a coalition of AI bots that coordinate against the player.
        /// </summary>
        private void FormCoalition(List<Player> aiPlayers)
        {
            // Select top aggressive + wealthy bots for the coalition
            var candidates = aiPlayers
                .Where(ai => ai.Strategy.CoalitionLoyalty > 0.4 && ai.Money > 20000m)
                .OrderByDescending(ai => ai.Strategy.AggressionLevel * ai.Strategy.CoalitionLoyalty * (double)(ai.Money / 10000m))
                .Take(GameConstants.AICoalitionSize)
                .Select(ai => ai.Id)
                .ToList();

            CoalitionMembers = candidates;
        }

        /// <summary>
        /// Check if an AI should perform a coordinated attack (coalition action).
        /// </summary>
        public bool IsCoalitionAction(Player ai)
        {
            if (!CoalitionMembers.Contains(ai.Id)) return false;
            return _random.NextDouble() < GameConstants.AICoalitionChance;
        }

        public bool IsHostileActionTriggered(Player ai, Player target, string itemName, NewsType newsType)
        {
            if (target.RivalPlayerIds.Contains(ai.Id))
            {
                // Escalation-scaled hostile chance
                var baseChance = target.CorporateThreatLevel * 0.3m * CurrentEscalationLevel;
                return _random.NextDouble() < (double)baseChance;
            }
            return false;
        }

        public decimal GetRivalPriceAdjustment(Player ai, Player target, MarketItem item)
        {
            if (!target.RivalPlayerIds.Contains(ai.Id)) return 1.0m;

            // Scale adjustment by escalation level
            decimal escalationMultiplier = 1.0m + (CurrentEscalationLevel * 0.05m);

            // If player is selling a lot of this item, rival will undercut by up to 15%
            var playerInv = target.Inventory.FirstOrDefault(i => i.ItemName == item.Name);
            if (playerInv != null && playerInv.Quantity > 500)
            {
                // Predatory Pricing: Sell low to drive player prices down
                return 0.85m / escalationMultiplier; // Even more aggressive at higher levels
            }

            // If player has a factory and needs this raw material, rival will overbid to starve them
            var isInputForPlayer = target.AutoProductionRecipes.Any(rId => 
                ProductionRecipes.AllRecipes.Any(r => r.RecipeId == rId && r.Inputs.ContainsKey(item.Name)));

            if (isInputForPlayer)
            {
                // Raw Lockout: Buy high to keep player from getting cheap raw materials
                return 1.25m * escalationMultiplier;
            }

            // Coalition members are extra aggressive
            if (CoalitionMembers.Contains(ai.Id) && CurrentEscalationLevel >= 3)
            {
                // Random market manipulation
                return _random.NextDouble() > 0.5 ? 1.30m : 0.70m;
            }

            return 1.0m;
        }

        /// <summary>
        /// Get sell volume multiplier for coordinated dump attacks.
        /// </summary>
        public decimal GetCoalitionDumpMultiplier(Player ai)
        {
            if (!CoalitionMembers.Contains(ai.Id)) return 1.0m;
            if (CurrentEscalationLevel < 2) return 1.0m;
            return GameConstants.AICoalitionDumpMultiplier;
        }
    }
}

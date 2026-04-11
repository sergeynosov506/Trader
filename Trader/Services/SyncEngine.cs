using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Models;

namespace EconomicGame.Services
{
    /// <summary>
    /// The SyncEngine manages the "Night Cycle" where individual experiences 
    /// are merged into the Collective Consciousness.
    /// </summary>
    public class SyncEngine
    {
        private readonly PlayerService _playerService;
        private static readonly Random _random = Random.Shared;

        public SyncEngine(PlayerService playerService)
        {
            _playerService = playerService;
        }

        /// <summary>
        /// Executes the Nightly Sync: Selection -> Crossover -> Mutation.
        /// </summary>
        public void PerformNightlySync()
        {
            var aiPlayers = _playerService.GetAllPlayers().Where(p => p.IsAI).ToList();
            if (aiPlayers.Count < 2) return;

            // 1. SELECTION: Identify the "Digital Elites" (Top 10%)
            // Phase 4: Meritocratic Elitism. We look at ROE (Return on Equity) + Success Score
            // This allows efficient smaller traders to influence the gene pool.
            var sortedAIs = aiPlayers
                .OrderByDescending(p => (p.DailyProfit / Math.Max(1000, p.Money)) * (1 + p.SuccessScore))
                .ToList();

            int eliteCount = Math.Max(1, aiPlayers.Count / 10);
            var elites = sortedAIs.Take(eliteCount).ToList();
            var struggling = sortedAIs.Skip(eliteCount).ToList();

            // 2. NEW BLOOD INJECTION (5% of population)
            // The weakest agents are formatted with completely new traits to prevent stagnation.
            int newBloodCount = Math.Max(1, aiPlayers.Count / 20);
            var failures = sortedAIs.TakeLast(newBloodCount);
            foreach (var failure in failures)
            {
                failure.Strategy = new GeneticStrategy(); // Fresh start
                failure.Strategy.Mutate(_random, 0.5);   // High intensity initial drift
            }

            // 3. KNOWLEDGE SYNTHESIS
            var globalStrategyPool = SynthesisGlobalStrategy(elites);
            ResolveMarketConflicts(aiPlayers, globalStrategyPool);

            // 4. PEER-TO-PEER LEARNING (Crossover)
            // Skip the new blood failures we just reformatted
            var crossoverCandidates = struggling.Except(failures).ToList();
            foreach (var subject in crossoverCandidates)
            {
                var teacher = elites[_random.Next(elites.Count)];
                subject.Strategy.Crossover(teacher.Strategy, 0.7);
                subject.Strategy.Mutate(_random, 0.05);
            }

            // 5. ACTIVE ROLE REINFORCEMENT & MIX TRAP BREAKOUT
            foreach (var ai in aiPlayers)
            {
                // Break the "MIX Trap" (0.4 - 0.6 specialization)
                // If they are stuck in the middle, apply a random nudge to force a choice
                if (ai.Strategy.MarketSpecialization > 0.45m && ai.Strategy.MarketSpecialization < 0.55m)
                {
                    ai.Strategy.MarketSpecialization += (decimal)(_random.NextDouble() * 0.4 - 0.2);
                }

                // Stronger Specialist Reinforcement
                if (ai.Strategy.MarketSpecialization > 0.6m) 
                    ai.Strategy.MarketSpecialization = Math.Min(1.0m, ai.Strategy.MarketSpecialization * 1.15m);
                else if (ai.Strategy.MarketSpecialization < 0.4m) 
                    ai.Strategy.MarketSpecialization = Math.Max(0.0m, ai.Strategy.MarketSpecialization * 0.85m);

                ResetDailyStats(ai);
            }
        }

        private void ResetDailyStats(Player ai)
        {
            ai.DailyProfit = 0;
            ai.TotalTrades = 0;
            ai.ProfitableTrades = 0;
        }

        private GeneticStrategy SynthesisGlobalStrategy(List<Player> elites)
        {
            var pool = new GeneticStrategy();
            
            pool.RiskTolerance = elites.Average(e => e.Strategy.RiskTolerance);
            pool.ProductionBias = elites.Average(e => e.Strategy.ProductionBias);
            pool.TimeHorizon = elites.Average(e => e.Strategy.TimeHorizon);
            
            pool.BuyThresholdRaw = elites.Average(e => e.Strategy.BuyThresholdRaw);
            pool.BuyThresholdProduct = elites.Average(e => e.Strategy.BuyThresholdProduct);
            pool.ProfitThreshold = elites.Average(e => e.Strategy.ProfitThreshold);
            pool.StopLossThreshold = elites.Average(e => e.Strategy.StopLossThreshold);
            pool.OvervaluedThreshold = elites.Average(e => e.Strategy.OvervaluedThreshold);

            return pool;
        }

        private void ResolveMarketConflicts(List<Player> players, GeneticStrategy globalPool)
        {
            // Probabilistic Conflict Resolution
            // If the market-wide average is negative, we dampen aggression in the collective pool
            // to steer future crossovers towards caution.
            
            decimal avgProfit = players.Average(p => p.DailyProfit);
            if (avgProfit < 0)
            {
                globalPool.RiskTolerance *= 0.9m;
                globalPool.StopLossThreshold *= 1.05m;
            }
        }

        private decimal Lerp(decimal a, decimal b, double t) => a + (decimal)t * (b - a);
        private double Lerp(double a, double b, double t) => a + t * (b - a);
    }
}

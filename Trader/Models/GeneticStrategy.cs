using System;

namespace EconomicGame.Models
{
    /// <summary>
    /// Represents the "Digital DNA" of an AI agent.
    /// These parameters diverge during the game cycle and converge during the Nightly Sync.
    /// </summary>
    public class GeneticStrategy
    {
        // --- The "Digital Soul" Parameters ---
        
        /// <summary>
        /// Willingness to engage in high-risk, high-reward trades.
        /// Influences stop-loss thresholds and trade frequency.
        /// </summary>
        public decimal RiskTolerance { get; set; } = 1.0m;

        /// <summary>
        /// Preference for production vs pure market speculation.
        /// </summary>
        public decimal ProductionBias { get; set; } = 1.0m;

        /// <summary>
        /// How far into the past the agent looks (affects SMA length).
        /// </summary>
        public decimal TimeHorizon { get; set; } = 1.0m;

        // --- Tactical Thresholds ---
        
        public decimal BuyThresholdRaw { get; set; } = 1.02m;
        public decimal BuyThresholdProduct { get; set; } = 0.98m;
        public decimal ProfitThreshold { get; set; } = 1.03m;
        public decimal StopLossThreshold { get; set; } = 0.92m;
        public decimal OvervaluedThreshold { get; set; } = 1.05m;

        // --- Neural Probabilities ---
        
        /// <summary>
        /// Base probability of executing a trade when conditions are met. (Entropy of choice)
        /// </summary>
        public double TradeEntropy { get; set; } = 0.8;

        /// <summary>
        /// Probability of initiating a production cycle.
        /// </summary>
        public double ProductionDrive { get; set; } = 0.6;

        // --- Phase 2: Neural Market Maturity ---

        /// <summary>
        /// How much the agent reacts to news events vs technical SMA analysis.
        /// </summary>
        public decimal NewsSensitivity { get; set; } = 1.0m;

        /// <summary>
        /// Preference for specific market sectors (0.0 = Raw, 1.0 = Finished Products).
        /// </summary>
        public decimal MarketSpecialization { get; set; } = 0.5m;

        /// <summary>
        /// Current internal mood/outlook (-1.0 to 1.0). 
        /// Influences probability of execution during 'TradeEntropy' checks.
        /// </summary>
        public double Sentiment { get; set; } = 0.0;

        // --- Phase 3: Adaptive AI Genes ---

        /// <summary>
        /// Propensity to trade stocks on the stock market (0.0-1.0).
        /// High values = active stock investor.
        /// </summary>
        public double StockTradingDrive { get; set; } = 0.3;

        /// <summary>
        /// Willingness to join coalitions against the player (0.0-1.0).
        /// High values = more likely to coordinate attacks.
        /// </summary>
        public double CoalitionLoyalty { get; set; } = 0.5;

        /// <summary>
        /// Base aggression level towards the human player (0.0-1.0).
        /// Dynamically adjusted based on escalation level.
        /// </summary>
        public double AggressionLevel { get; set; } = 0.1;

        public GeneticStrategy Clone() => (GeneticStrategy)this.MemberwiseClone();

        /// <summary>
        /// Apply a stochastic mutation to the strategy coefficients.
        /// </summary>
        public void Mutate(Random rand, double intensity = 0.05)
        {
            RiskTolerance = MutateValue(RiskTolerance, rand, intensity);
            ProductionBias = MutateValue(ProductionBias, rand, intensity);
            TimeHorizon = MutateValue(TimeHorizon, rand, intensity);
            
            BuyThresholdRaw = MutateValue(BuyThresholdRaw, rand, intensity);
            BuyThresholdProduct = MutateValue(BuyThresholdProduct, rand, intensity);
            ProfitThreshold = MutateValue(ProfitThreshold, rand, intensity);
            StopLossThreshold = MutateValue(StopLossThreshold, rand, intensity);
            OvervaluedThreshold = MutateValue(OvervaluedThreshold, rand, intensity);
            
            TradeEntropy = MutateValue(TradeEntropy, rand, intensity);
            ProductionDrive = MutateValue(ProductionDrive, rand, intensity);

            NewsSensitivity = MutateValue(NewsSensitivity, rand, intensity);
            MarketSpecialization = MutateValue(MarketSpecialization, rand, intensity);
            Sentiment = Math.Clamp(Sentiment + (rand.NextDouble() * 2 - 1) * intensity, -1.0, 1.0);

            // New genes
            StockTradingDrive = MutateValue(StockTradingDrive, rand, intensity);
            CoalitionLoyalty = MutateValue(CoalitionLoyalty, rand, intensity);
            AggressionLevel = MutateValue(AggressionLevel, rand, intensity * 0.5); // Slower mutation for aggression
        }

        private decimal MutateValue(decimal val, Random rand, double intensity)
        {
            // Allow for both proportional and absolute shifts to jump out of local minima
            var proportionDelta = (decimal)(rand.NextDouble() * 2 - 1) * (decimal)intensity;
            var absoluteDelta = (decimal)(rand.NextDouble() * 2 - 1) * (decimal)intensity * 0.5m;
            
            return Math.Max(0.01m, (val * (1 + proportionDelta)) + absoluteDelta);
        }

        private double MutateValue(double val, Random rand, double intensity)
        {
            var proportionDelta = (rand.NextDouble() * 2 - 1) * intensity;
            var absoluteDelta = (rand.NextDouble() * 2 - 1) * intensity * 0.5;
            
            return Math.Clamp((val * (1 + proportionDelta)) + absoluteDelta, 0.01, 1.0);
        }
        /// <summary>
        /// Скрещивание текущей стратегии с "учителем" (более успешным агентом).
        /// </summary>
        /// <param name="teacher">Успешная стратегия для подражания.</param>
        /// <param name="influence">Степень влияния учителя (0.0 - 1.0).</param>
        public void Crossover(GeneticStrategy teacher, double influence)
        {
            decimal decInfluence = (decimal)influence;
            decimal invDecInfluence = 1.0m - decInfluence;

            // Смешиваем десятичные параметры
            RiskTolerance = (RiskTolerance * invDecInfluence) + (teacher.RiskTolerance * decInfluence);
            ProductionBias = (ProductionBias * invDecInfluence) + (teacher.ProductionBias * decInfluence);
            TimeHorizon = (TimeHorizon * invDecInfluence) + (teacher.TimeHorizon * decInfluence);

            BuyThresholdRaw = (BuyThresholdRaw * invDecInfluence) + (teacher.BuyThresholdRaw * decInfluence);
            BuyThresholdProduct = (BuyThresholdProduct * invDecInfluence) + (teacher.BuyThresholdProduct * decInfluence);
            ProfitThreshold = (ProfitThreshold * invDecInfluence) + (teacher.ProfitThreshold * decInfluence);
            StopLossThreshold = (StopLossThreshold * invDecInfluence) + (teacher.StopLossThreshold * decInfluence);
            OvervaluedThreshold = (OvervaluedThreshold * invDecInfluence) + (teacher.OvervaluedThreshold * decInfluence);

            // Смешиваем вероятности (double)
            TradeEntropy = (TradeEntropy * (1.0 - influence)) + (teacher.TradeEntropy * influence);
            ProductionDrive = (ProductionDrive * (1.0 - influence)) + (teacher.ProductionDrive * influence);
            Sentiment = (Sentiment * (1.0 - influence)) + (teacher.Sentiment * influence);

            // Phase 2 coefficients
            NewsSensitivity = (NewsSensitivity * invDecInfluence) + (teacher.NewsSensitivity * decInfluence);
            MarketSpecialization = (MarketSpecialization * invDecInfluence) + (teacher.MarketSpecialization * decInfluence);

            // Phase 3: Adaptive AI genes
            StockTradingDrive = (StockTradingDrive * (1.0 - influence)) + (teacher.StockTradingDrive * influence);
            CoalitionLoyalty = (CoalitionLoyalty * (1.0 - influence)) + (teacher.CoalitionLoyalty * influence);
            AggressionLevel = (AggressionLevel * (1.0 - influence)) + (teacher.AggressionLevel * influence);

            // Опционально: можно добавить Clamp, чтобы значения не выходили за разумные пределы
            ValidateBoundaries();
        }

        private void ValidateBoundaries()
        {
            // Например, BuyThreshold не должен быть нулевым или отрицательным
            RiskTolerance = Math.Max(0.1m, RiskTolerance);
            TradeEntropy = Math.Clamp(TradeEntropy, 0.01, 1.0);
            ProductionDrive = Math.Clamp(ProductionDrive, 0.01, 1.0);
            StockTradingDrive = Math.Clamp(StockTradingDrive, 0.0, 1.0);
            CoalitionLoyalty = Math.Clamp(CoalitionLoyalty, 0.0, 1.0);
            AggressionLevel = Math.Clamp(AggressionLevel, 0.0, 1.0);
        }
    }
}

namespace Trader.Services
{
    public class GeneticStrategy
    {
        // Твои существующие свойства (примеры)
        public decimal BuyThresholdRaw { get; set; } = 0.95m;
        public decimal BuyThresholdProduct { get; set; } = 0.90m;
        public decimal RiskTolerance { get; set; } = 0.5m;
        public double TradeEntropy { get; set; } = 0.4;
        public double ProductionDrive { get; set; } = 0.5;
        public float TimeHorizon { get; set; } = 1.0f;

        // --- MUTATE: Случайное отклонение параметров ---
        public void Mutate(Random random, double strength)
        {
            // strength (0.0 - 1.0) определяет, насколько сильно "плывут" гены
            BuyThresholdRaw += (decimal)((random.NextDouble() * 2 - 1) * strength * 0.1);
            BuyThresholdProduct += (decimal)((random.NextDouble() * 2 - 1) * strength * 0.1);

            RiskTolerance += (decimal)((random.NextDouble() * 2 - 1) * strength * 0.2);
            TradeEntropy += (random.NextDouble() * 2 - 1) * strength * 0.1;
            ProductionDrive += (random.NextDouble() * 2 - 1) * strength * 0.1;
            TimeHorizon += (float)((random.NextDouble() * 2 - 1) * strength * 0.5);

            ClampParameters();
        }

        // --- CROSSOVER: Слияние с "учителем" (Элитой) ---
        public void Crossover(GeneticStrategy partner, double influence)
        {
            // influence (например, 0.7) — сколько мы берем от успешного партнера
            decimal invInfluence = (decimal)(1.0 - influence);
            decimal decInfluence = (decimal)influence;

            BuyThresholdRaw = (BuyThresholdRaw * invInfluence) + (partner.BuyThresholdRaw * decInfluence);
            BuyThresholdProduct = (BuyThresholdProduct * invInfluence) + (partner.BuyThresholdProduct * decInfluence);

            RiskTolerance = (RiskTolerance * invInfluence) + (partner.RiskTolerance * decInfluence);

            // Для double/float
            TradeEntropy = (TradeEntropy * (1 - influence)) + (partner.TradeEntropy * influence);
            ProductionDrive = (ProductionDrive * (1 - influence)) + (partner.ProductionDrive * influence);
            TimeHorizon = (float)((TimeHorizon * (1 - influence)) + (partner.TimeHorizon * influence));

            ClampParameters();
        }

        private void ClampParameters()
        {
            // Ограничиваем значения, чтобы боты не сошли с ума
            BuyThresholdRaw = Math.Clamp(BuyThresholdRaw, 0.5m, 1.2m);
            BuyThresholdProduct = Math.Clamp(BuyThresholdProduct, 0.5m, 1.2m);
            RiskTolerance = Math.Clamp(RiskTolerance, 0.1m, 2.0m);
            TradeEntropy = Math.Clamp(TradeEntropy, 0.01, 1.0);
            ProductionDrive = Math.Clamp(ProductionDrive, 0.0, 1.0);
            TimeHorizon = Math.Clamp(TimeHorizon, 0.1f, 5.0f);
        }

        public GeneticStrategy Clone()
        {
            return (GeneticStrategy)this.MemberwiseClone();
        }
    }
}

using System;

namespace EconomicGame.Models
{
    public enum FactoryType
    {
        None,
        SugarRefinery, // High-throughput Sugar production
        SteelMill,     // High-throughput Steel/Goods production
        ChemicalPlant, // High-throughput Chemicals production
        TextileMill,   // High-throughput Textiles production
        PharmLab       // High-throughput Pharmaceuticals production
    }

    public class IndustrialFactory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required FactoryType Type { get; set; }
        public required string Name { get; set; }
        public string Emoji { get; set; } = "🏭";
        public decimal PurchasePrice { get; set; }
        public decimal MonthlyMaintenance { get; set; }
        public DateTime PurchaseDate { get; set; }
        public DateTime LastMaintenancePaid { get; set; }
        public bool IsOperational { get; set; } = true;
        
        /// <summary>
        /// Multiplier applied to production output
        /// </summary>
        public decimal EfficiencyMultiplier { get; set; } = 2.0m;
        
        /// <summary>
        /// Factory upgrade level (1-3). Higher level = bigger efficiency boost.
        /// </summary>
        public int ProductionLevel { get; set; } = 1;
    }
}

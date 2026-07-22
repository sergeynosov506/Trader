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
        PharmLab,      // High-throughput Pharmaceuticals production
        
        SugarCanePlantation, // Produces SugarCane
        CoffeePlantation,    // Produces CoffeeBeans
        WheatFarm            // Produces Wheat
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

        // --- Agriculture harvest cycles (plantations/farms only) ---

        /// <summary>
        /// When the current growth cycle started. Null = not started yet (set on first tick).
        /// </summary>
        public DateTime? CurrentCycleStart { get; set; }

        /// <summary>
        /// The crop caught a disease this cycle. Cured automatically at harvest
        /// if the owner has enough Chemicals in stock; otherwise the yield suffers.
        /// </summary>
        public bool IsDiseased { get; set; }
    }
}

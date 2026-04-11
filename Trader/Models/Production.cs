using EconomicGame.Configuration;

namespace EconomicGame
{
    /// <summary>
    /// Represents a production recipe that converts input materials into outputs
    /// </summary>
    public class ProductionRecipe
    {
        public Guid RecipeId { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
        public required string Description { get; set; }
        public string Emoji { get; set; } = "🏭";
        
        /// <summary>
        /// Input materials required: ItemName -> Quantity
        /// </summary>
        public required Dictionary<string, int> Inputs { get; set; }
        
        /// <summary>
        /// Output products created: ItemName -> Quantity
        /// </summary>
        public required Dictionary<string, int> Outputs { get; set; }
        
        /// <summary>
        /// Time in game ticks to complete production
        /// </summary>
        public int ProductionTime { get; set; } = 1;
        
        /// <summary>
        /// Production cost (labor, energy, etc)
        /// </summary>
        public decimal ProductionCost { get; set; } = 0;
    }

    /// <summary>
    /// Static class containing all available production recipes
    /// </summary>
    public static class ProductionRecipes
    {
        public static List<ProductionRecipe> AllRecipes { get; } = new List<ProductionRecipe>
        {
            // Wheat -> Bread
            new ProductionRecipe
            {
                Name = "Хлеб",
                Description = "Превратите пшеницу в хлеб",
                Emoji = "🍞",
                Inputs = new Dictionary<string, int> { { GameConstants.Wheat, 10 } },
                Outputs = new Dictionary<string, int> { { GameConstants.Bread, 8 } },
                ProductionTime = 1,
                ProductionCost = 50m
            },
            
            // Steel + Oil -> Goods (Machinery)
            new ProductionRecipe
            {
                Name = "Оборудование",
                Description = "Производство оборудования из стали и масла",
                Emoji = "⚙️",
                Inputs = new Dictionary<string, int> { { GameConstants.Steel, 5 }, { GameConstants.Oil, 3 } },
                Outputs = new Dictionary<string, int> { { GameConstants.Goods, 4 } },
                ProductionTime = 2,
                ProductionCost = 100m
            },
            
            // Copper + Gold -> Electronics
            new ProductionRecipe
            {
                Name = "Электроника",
                Description = "Производство электроники",
                Emoji = "📱",
                Inputs = new Dictionary<string, int> { { GameConstants.Copper, 8 }, { GameConstants.Gold, 2 } },
                Outputs = new Dictionary<string, int> { { GameConstants.Electronics, 3 } },
                ProductionTime = 3,
                ProductionCost = 200m
            },
            
            // Wheat + Oil -> Fuel (Biofuel)
            new ProductionRecipe
            {
                Name = "Биотопливо",
                Description = "Производство биотоплива",
                Emoji = "⛽",
                Inputs = new Dictionary<string, int> { { GameConstants.Wheat, 20 }, { GameConstants.Oil, 5 } },
                Outputs = new Dictionary<string, int> { { GameConstants.Fuel, 10 } },
                ProductionTime = 2,
                ProductionCost = 150m
            },

            // SugarCane -> Sugar
            new ProductionRecipe
            {
                Name = "Сахар",
                Description = "Переработка сахарного тростника",
                Emoji = "🍬",
                Inputs = new Dictionary<string, int> { { GameConstants.SugarCane, 15 } },
                Outputs = new Dictionary<string, int> { { GameConstants.Sugar, 10 } },
                ProductionTime = 1,
                ProductionCost = 30m
            },

            // CoffeeBeans -> Coffee
            new ProductionRecipe
            {
                Name = "Кофе",
                Description = "Обжарка и упаковка кофейных зёрен",
                Emoji = "☕",
                Inputs = new Dictionary<string, int> { { GameConstants.CoffeeBeans, 5 } },
                Outputs = new Dictionary<string, int> { { GameConstants.Coffee, 2 } },
                ProductionTime = 2,
                ProductionCost = 80m
            },

            // === NEW: Advanced Production Recipes ===

            // Oil + Copper -> Chemicals
            new ProductionRecipe
            {
                Name = "Химикаты",
                Description = "Синтез химических соединений из нефти и меди",
                Emoji = "🧪",
                Inputs = new Dictionary<string, int> { { GameConstants.Oil, GameConstants.ChemicalsOilInput }, { GameConstants.Copper, GameConstants.ChemicalsCopperInput } },
                Outputs = new Dictionary<string, int> { { GameConstants.Chemicals, GameConstants.ChemicalsOutput } },
                ProductionTime = 3,
                ProductionCost = GameConstants.ChemicalsProductionCost
            },

            // Chemicals + SugarCane -> Pharmaceuticals
            new ProductionRecipe
            {
                Name = "Фармацевтика",
                Description = "Производство лекарственных препаратов",
                Emoji = "💊",
                Inputs = new Dictionary<string, int> { { GameConstants.Chemicals, GameConstants.PharmChemicalsInput }, { GameConstants.SugarCane, GameConstants.PharmSugarCaneInput } },
                Outputs = new Dictionary<string, int> { { GameConstants.Pharmaceuticals, GameConstants.PharmOutput } },
                ProductionTime = 4,
                ProductionCost = GameConstants.PharmaceuticalsProductionCost
            },

            // Steel + Gold + Electronics -> LuxuryGoods
            new ProductionRecipe
            {
                Name = "Люкс-товары",
                Description = "Производство элитных товаров из стали, золота и электроники",
                Emoji = "💎",
                Inputs = new Dictionary<string, int> { 
                    { GameConstants.Steel, GameConstants.LuxurySteelInput }, 
                    { GameConstants.Gold, GameConstants.LuxuryGoldInput },
                    { GameConstants.Electronics, GameConstants.LuxuryElectronicsInput }
                },
                Outputs = new Dictionary<string, int> { { GameConstants.LuxuryGoods, GameConstants.LuxuryOutput } },
                ProductionTime = 5,
                ProductionCost = GameConstants.LuxuryGoodsProductionCost
            },

            // Wheat + CoffeeBeans -> Textiles
            new ProductionRecipe
            {
                Name = "Текстиль",
                Description = "Производство текстильной продукции",
                Emoji = "🧵",
                Inputs = new Dictionary<string, int> { { GameConstants.Wheat, GameConstants.TextilesWheatInput }, { GameConstants.CoffeeBeans, GameConstants.TextilesCoffeeBeansInput } },
                Outputs = new Dictionary<string, int> { { GameConstants.Textiles, GameConstants.TextilesOutput } },
                ProductionTime = 2,
                ProductionCost = GameConstants.TextilesProductionCost
            }
        };
    }

    /// <summary>
    /// Represents an active production job
    /// </summary>
    public class ProductionJob
    {
        public Guid JobId { get; set; } = Guid.NewGuid();
        public required ProductionRecipe Recipe { get; set; }
        public int Quantity { get; set; } = 1;
        public DateTime StartTime { get; set; }
        public int TicksRemaining { get; set; }
        public bool IsComplete => TicksRemaining <= 0;
    }
}

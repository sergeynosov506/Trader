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
        /// <summary>
        /// RecipeIds must be STABLE across app restarts: they are persisted in saves
        /// (AutoProductionRecipes) and would silently break auto-production for both
        /// the player and the bots if regenerated each run. Derived from the recipe name.
        /// </summary>
        static ProductionRecipes()
        {
            foreach (var recipe in AllRecipes)
            {
                var hash = System.Security.Cryptography.MD5.HashData(
                    System.Text.Encoding.UTF8.GetBytes("trader-recipe:" + recipe.Name));
                recipe.RecipeId = new Guid(hash);
            }
        }

        public static List<ProductionRecipe> AllRecipes { get; } = new List<ProductionRecipe>
        {
            // Wheat -> Bread
            new ProductionRecipe
            {
                Name = "Хлеб",
                Description = "Превратите пшеницу в хлеб",
                Emoji = "🍞",
                Inputs = new Dictionary<string, int> { { GameConstants.Wheat, GameConstants.BreadWheatInput } },
                Outputs = new Dictionary<string, int> { { GameConstants.Bread, GameConstants.BreadOutput } },
                ProductionTime = 4,
                ProductionCost = GameConstants.BreadProductionCost
            },
            
            // Steel + Oil -> Goods (Machinery)
            new ProductionRecipe
            {
                Name = "Оборудование",
                Description = "Производство оборудования из стали и масла",
                Emoji = "⚙️",
                Inputs = new Dictionary<string, int> { { GameConstants.Steel, GameConstants.GoodsSteelInput }, { GameConstants.Oil, GameConstants.GoodsOilInput } },
                Outputs = new Dictionary<string, int> { { GameConstants.Goods, GameConstants.GoodsOutput } },
                ProductionTime = 8,
                ProductionCost = GameConstants.GoodsProductionCost
            },
            
            // Copper + Gold -> Electronics
            new ProductionRecipe
            {
                Name = "Электроника",
                Description = "Производство электроники",
                Emoji = "📱",
                Inputs = new Dictionary<string, int> { { GameConstants.Copper, GameConstants.ElectronicsCopperInput }, { GameConstants.Gold, GameConstants.ElectronicsGoldInput } },
                Outputs = new Dictionary<string, int> { { GameConstants.Electronics, GameConstants.ElectronicsOutput } },
                ProductionTime = 12,
                ProductionCost = GameConstants.ElectronicsProductionCost
            },
            
            // Wheat + Oil -> Fuel (Biofuel)
            new ProductionRecipe
            {
                Name = "Биотопливо",
                Description = "Производство биотоплива",
                Emoji = "⛽",
                Inputs = new Dictionary<string, int> { { GameConstants.Wheat, 20 }, { GameConstants.Oil, 5 } },
                Outputs = new Dictionary<string, int> { { GameConstants.Fuel, 10 } },
                ProductionTime = 8,
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
                ProductionTime = 16,
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
                ProductionTime = 8,
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
                ProductionTime = 12,
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
                ProductionTime = 16,
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
                ProductionTime = 20,
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
                ProductionTime = 8,
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

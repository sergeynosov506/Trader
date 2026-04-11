namespace EconomicGame.Configuration
{
    public static class GameConstants
    {
        // Player Settings
        public const decimal InitialPlayerMoney = 10000m;
        
        // Loan Settings
        public const decimal DefaultInterestRate = 0.05m; // 5% annual
        public const decimal DailyPenaltyRate = 0.01m; // 1% per day
        public const decimal MaxPenaltyThreshold = 0.5m; // 50% of loan amount
        
        // News Settings
        public const double NewsGenerationChance = 0.1; // 10% chance
        public const decimal MaxNewsImpact = 0.1m; // ±10%
        
        // Price Update Settings
        public const int MaxDemandSupplyRange = 5;
        public const decimal DemandSupplyImpact = 0.01m; // 1% per point
        public const decimal PriceRandomnessRange = 0.03m; // ±1.5%
        public const int MaxPriceHistoryDays = 60; // Double history for smoother long-term averages
        public const decimal MinPrice = 0.05m;
        public const decimal MaxPriceChangePerTick = 0.15m; // ±15% max move
        public const decimal MarketImpactDivisor = 100m;    // 100 units moved = 1% price shift (was 500)
        public const int ScarcityThreshold = 200;           // Below this quantity, price feels pressure
        public const decimal ScarcityImpact = 0.015m;        // Up to 1.5% upward pressure from scarcity (was 5%)
        public const decimal InflationCoolingFactor = 0.98m; // Prices > $1000 shrink by 2% per tick
        public const decimal PriceHyperThreshold = 500m;   // Threshold for "Hyper-inflation" (was 1000)
        
        // Game Tick Settings
        public const int GameTickIntervalSeconds = 5;
        
        // Trading Settings
        public const decimal CarPrice = 2000m;
        public const decimal SellerFeeRate = 0.10m;           // 10% market commission
        public const decimal CancellationPenaltyRate = 0.05m; // 5% fee for cancelling listing
        public const int MaxActivityLogEntries = 50;
        public const int MaxNewsEntries = 10;
        
        // Event Settings
        public const double EventGenerationChance = 0.02;     // 2% chance per tick
        public const int EventExpirationMinutes = 60;         // 1 hour game time
        
        // Reputation Settings
        public const int InitialReputation = 50;              // 0-100 scale
        public const int ReputationGainPerTrade = 1;
        public const int ReputationLossPerCancellation = 5;
        public const decimal MaxReputationDiscount = 0.10m;   // Up to 10% interest discount
        
        // Bar Settings
        public const decimal BeerPrice = 25m;
        public const decimal WhiskeyPrice = 50m;
        public const decimal CocktailPrice = 75m;
        public const int SoberUpMinutes = 120;                // 2 hours game time to sober up per drink
        public const double BarEncounterChance = 0.4;         // 40% chance to meet someone
        public const double DrunkBadDecisionChance = 0.3;     // 30% chance for drunk mistakes
        public const decimal GamblingMinBet = 50m;
        public const decimal GamblingMaxBet = 500m;
        
        // Housing Settings
        public const decimal SmallRoomPrice = 500m;
        public const decimal SmallRoomRent = 50m;
        public const int SmallRoomCapacity = 2;
        public const decimal SmallRoomBirthdayBonus = 50m;
        
        public const decimal ApartmentPrice = 5000m;
        public const decimal ApartmentRent = 200m;
        public const int ApartmentCapacity = 5;
        public const decimal ApartmentBirthdayBonus = 200m;
        
        public const decimal HousePrice = 25000m;
        public const decimal HouseRent = 500m;
        public const int HouseCapacity = 10;
        public const decimal HouseBirthdayBonus = 500m;
        
        public const decimal MansionPrice = 100000m;
        public const decimal MansionRent = 1000m;
        public const int MansionCapacity = 25;
        public const decimal MansionBirthdayBonus = 2000m;
        
        public const int RentDueGameDays = 30;  // Rent due every 30 game days
        
        // Vehicle Settings
        public const decimal BasicCarPrice = 2000m;
        public const int BasicCarCapacity = 250;      // Increased from 50
        
        public const decimal VanPrice = 5000m;
        public const int VanCapacity = 750;           // Increased from 150
        
        public const decimal TruckPrice = 15000m;
        public const int TruckCapacity = 2500;         // Increased from 500
        
        public const decimal SemiTruckPrice = 50000m;
        public const int SemiTruckCapacity = 10000;    // Increased from 2000
        
        // Land Settings
        public const decimal SmallPlotPrice = 10000m;
        public const int SmallPlotMaxWarehouse = 1;  // MiniWarehouse
        
        public const decimal MediumPlotPrice = 30000m;
        public const int MediumPlotMaxWarehouse = 2; // Warehouse
        
        public const decimal LargePlotPrice = 80000m;
        public const int LargePlotMaxWarehouse = 3;  // LargeWarehouse
        
        // Warehouse Settings
        public const decimal MiniWarehousePrice = 8000m;
        public const int MiniWarehouseCapacity = 1000; // Increased from 200
        public const decimal MiniWarehouseMaintenance = 100m;
        
        public const decimal WarehousePrice = 25000m;
        public const int WarehouseCapacity = 5000;     // Increased from 1000
        public const decimal WarehouseMaintenance = 300m;
        
        public const decimal LargeWarehousePrice = 75000m;
        public const int LargeWarehouseCapacity = 25000; // Increased from 5000
        public const decimal LargeWarehouseMaintenance = 800m;
        
        // NEW: Industrial Complex & Trade Hub (advanced warehouses for specialized paths)
        public const decimal IndustrialComplexPrice = 200000m;
        public const int IndustrialComplexCapacity = 50000;
        public const decimal IndustrialComplexMaintenance = 2000m;
        
        public const decimal TradeHubPrice = 350000m;
        public const int TradeHubCapacity = 100000;
        public const decimal TradeHubMaintenance = 3500m;
        
        public const int WarehouseMaintenanceDays = 30;
        
        // Trading License Settings (Trader Path)
        public const decimal TradingLicenseLevel1Cost = 15000m;
        public const decimal TradingLicenseLevel2Cost = 50000m;
        public const decimal TradingLicenseLevel3Cost = 150000m;
        public const int MaxWarehousesLevel0 = 1;   // No license
        public const int MaxWarehousesLevel1 = 2;   // Basic license
        public const int MaxWarehousesLevel2 = 3;   // Advanced license
        public const int MaxWarehousesLevel3 = 5;   // Master license
        public const decimal BulkDiscount5Percent = 0.05m;   // 5% discount for orders > 100 units
        public const decimal BulkDiscount10Percent = 0.10m;  // 10% discount for orders > 500 units
        
        // Industrial Assets Settings
        public const decimal SugarRefineryPrice = 250000m;
        public const decimal SugarRefineryMaintenance = 5000m;
        public const decimal SteelMillPrice = 500000m;
        public const decimal SteelMillMaintenance = 10000m;
        public const decimal ProductionEfficiencyBoost = 2.0m; // Factories produce 2x goods
        
        // NEW: Advanced Factories (Producer Path)
        public const decimal ChemicalPlantPrice = 400000m;
        public const decimal ChemicalPlantMaintenance = 8000m;
        public const decimal TextileMillPrice = 300000m;
        public const decimal TextileMillMaintenance = 6000m;
        public const decimal PharmLabPrice = 750000m;
        public const decimal PharmLabMaintenance = 15000m;
        
        // Overload Penalty Settings
        public const double OverloadLossChance = 0.3;      // 30% chance per tick when overloaded
        public const double OverloadLossPercent = 0.1;     // Lose 10% of excess cargo
        
        // Bank Deposit Settings
        public const decimal DepositInterestMultiplier = 0.4m;  // Deposits earn 40% of loan rate
        public const int DepositInterestPaymentDays = 7;         // Interest paid every 7 days
        public const int MonthlyReportDays = 30;                 // Generate report every 30 days
        public const int MaxMonthlyReports = 12;                 // Keep last 12 reports
        
        // AI Trading Settings - AGGRESSIVE
        public const decimal AIMinCashBuffer = 500m;              // Lower buffer to trade more
        public const decimal AIBuyThresholdRaw = 1.05m;           // Buy even at 105% (was 102)
        public const decimal AIBuyThresholdProduct = 0.95m;       // Buy products below 95% (was 98)
        public const decimal AIPortfolioPercentPerTrade = 0.30m;  // 30% of money per trade (was 15%)
        public const int AIMaxQuantityPerTrade = 50;              // 50 items per trade (was 15)
        public const int AIWealthyTradeMultiplier = 4;            // Rich AI trade 4x quantities (200 units)
        public const decimal AIWealthyThreshold = 50000m;         // AI considered "Wealthy" for scaling
        public const double AITradeChance = 0.8;                  // 80% chance to trade (was 50%)
        public const double AIProductSellChance = 0.8;            // 80% chance to sell products
        public const double AIRawSellChance = 0.6;                // 60% chance to sell raw (was 40%)
        public const decimal AIProfitThreshold = 1.03m;           // Sell at 3% profit (was 8%)
        public const decimal AIOvervaluedThreshold = 1.10m;       // Sell when 10% above SMA (was 1.02)
        public const decimal AIStopLossThreshold = 0.92m;         // Stop loss at 8% (was 15%)
        public const double AIStopLossChance = 0.5;               // 50% chance to cut losses (was 20%)
        public const double AIProductionChance = 0.6;             // 60% chance to produce (was 30%)
        public const int AISMALength = 15;                        // Slower 15-tick SMA for stability (was 3)
        
        // AI Escalation Settings (Adaptive Aggression)
        public const decimal AIInitialMoney = 50000m;             // Increased from 10000
        public const double AIStartWithAssetsChance = 0.20;       // 20% of bots start with assets
        public const decimal EscalationLevel1Threshold = 50000m;  // Competitive
        public const decimal EscalationLevel2Threshold = 200000m; // Aggressive
        public const decimal EscalationLevel3Threshold = 500000m; // Hostile
        public const int AICoalitionSize = 5;                     // Max bots in a coalition
        public const double AICoalitionChance = 0.3;              // 30% chance to form/join coalition
        public const decimal AICoalitionDumpMultiplier = 3.0m;    // Coalition dumps 3x volume
        public const double AIStockTradeChance = 0.4;             // 40% chance AI trades stocks per tick
        
        // Production Settings
        public const decimal BreadProductionCost = 50m;
        public const int BreadWheatInput = 10;
        public const int BreadOutput = 8;
        
        public const decimal GoodsProductionCost = 100m;
        public const int GoodsSteelInput = 5;
        public const int GoodsOilInput = 3;
        public const int GoodsOutput = 4;
        
        public const decimal ElectronicsProductionCost = 200m;
        public const int ElectronicsCopperInput = 8;
        public const int ElectronicsGoldInput = 2;
        public const int ElectronicsOutput = 3;
        
        public const decimal FuelProductionCost = 150m;
        public const int FuelWheatInput = 20;
        public const int FuelOilInput = 5;
        public const int FuelOutput = 10;
        
        // NEW: Advanced Production Recipes
        public const decimal ChemicalsProductionCost = 250m;
        public const int ChemicalsOilInput = 10;
        public const int ChemicalsCopperInput = 5;
        public const int ChemicalsOutput = 6;
        
        public const decimal PharmaceuticalsProductionCost = 500m;
        public const int PharmChemicalsInput = 8;
        public const int PharmSugarCaneInput = 10;
        public const int PharmOutput = 3;
        
        public const decimal LuxuryGoodsProductionCost = 400m;
        public const int LuxurySteelInput = 3;
        public const int LuxuryGoldInput = 5;
        public const int LuxuryElectronicsInput = 2;
        public const int LuxuryOutput = 2;
        
        public const decimal TextilesProductionCost = 120m;
        public const int TextilesWheatInput = 15;
        public const int TextilesCoffeeBeansInput = 5;
        public const int TextilesOutput = 8;
        
        // Stock Market Settings (Investor Path)
        public const int StockMarketOpenHour = 9;
        public const int StockMarketCloseHour = 17;
        public const decimal StockBrokerFee = 0.02m;             // 2% commission
        public const int DividendPaymentDays = 30;                // Dividends every 30 game days
        public const decimal MinDividendYield = 0.01m;            // 1% min yield
        public const decimal MaxDividendYield = 0.08m;            // 8% max yield
        public const decimal StockPriceCorrelation = 0.6m;        // 60% commodity correlation
        public const decimal StockPriceNoise = 0.04m;             // ±4% random noise
        public const int MaxStockPriceHistory = 60;
        public const int InitialSharesPerStock = 10000;
        
        // Gambling Settings
        public const int DiceRollsPerGame = 3;                   // Rerolls allowed
        public const decimal SlotJackpotMultiplier = 10m;        // 3 same symbols
        public const decimal SlotTwoMatchMultiplier = 2m;        // 2 same symbols
        public const int BlackjackDealerStandValue = 17;         // Dealer stands on 17
        public const decimal BlackjackWinMultiplier = 2m;        // Win pays 2x
        public const decimal BlackjackBlackjackMultiplier = 2.5m; // Blackjack pays 2.5x
        
        // Dice Yahtzee Payouts
        public const decimal DicePairMultiplier = 1.5m;
        public const decimal DiceTwoPairsMultiplier = 2m;
        public const decimal DiceThreeOfKindMultiplier = 3m;
        public const decimal DiceStraightMultiplier = 4m;
        public const decimal DiceFullHouseMultiplier = 5m;
        public const decimal DiceFourOfKindMultiplier = 8m;
        public const decimal DiceFiveOfKindMultiplier = 15m;
        
        // Item Names
        public const string Wheat = "Wheat";
        public const string Steel = "Steel";
        public const string Oil = "Oil";
        public const string Gold = "Gold";
        public const string Copper = "Copper";
        public const string SugarCane = "SugarCane";
        public const string CoffeeBeans = "CoffeeBeans";
        public const string Bread = "Bread";
        public const string Goods = "Goods";
        public const string Electronics = "Electronics";
        public const string Fuel = "Fuel";
        public const string Sugar = "Sugar";
        public const string Coffee = "Coffee";
        
        // NEW: Advanced Item Names
        public const string Chemicals = "Chemicals";
        public const string Pharmaceuticals = "Pharmaceuticals";
        public const string LuxuryGoods = "LuxuryGoods";
        public const string Textiles = "Textiles";

        // Item Categories (for grouping)
        public static readonly string[] RawMaterials = { Steel, Oil, Gold, Wheat, Copper, SugarCane, CoffeeBeans };
        public static readonly string[] Products = { Bread, Goods, Electronics, Fuel, Sugar, Coffee, Chemicals, Pharmaceuticals, LuxuryGoods, Textiles };
    }
}

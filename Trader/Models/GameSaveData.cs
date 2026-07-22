using System;
using System.Collections.Generic;
using EconomicGame.Models;

namespace EconomicGame.Models
{
    /// <summary>
    /// Complete game state for saving/loading
    /// </summary>
    public class GameSaveData
    {
        public string SaveName { get; set; } = "Автосохранение";
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public DateTime GameTime { get; set; }
        public int GameDay { get; set; }
        
        // Player data
        public PlayerSaveData? PlayerData { get; set; }
        
        // AI Ecosystem
        public List<PlayerSaveData> AIPlayers { get; set; } = new();
        
        // Market State
        public List<MarketItemSave> MarketItems { get; set; } = new();
        
        // Stock Market State
        public List<StockSaveData> Stocks { get; set; } = new();
        public DateTime LastDividendPaid { get; set; }

        // Bar cash desk (economy rebalance). Defaults keep old saves compatible.
        public decimal BarBankroll { get; set; } = 25000m;
    }

    public class PlayerSaveData
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Money { get; set; }
        public int Reputation { get; set; }
        
        // Inventory
        public List<InventoryItemSave> Inventory { get; set; } = new();
        
        // Vehicle
        public VehicleSave? Vehicle { get; set; }
        public List<VehicleSave> Vehicles { get; set; } = new();
        
        // Property
        public PropertySave? Property { get; set; }
        public List<PropertySave> Properties { get; set; } = new();
        
        // Land & Warehouses (multi-warehouse)
        public LandSave? Land { get; set; }
        public List<LandSave> Lands { get; set; } = new();
        public List<WarehouseSave> Warehouses { get; set; } = new();
        public List<IndustrialFactorySave> Factories { get; set; } = new();
        
        // Trading License
        public int TradingLicenseLevel { get; set; }
        public decimal TradeVolume { get; set; }
        
        // Loans
        public List<LoanSave> Loans { get; set; } = new();
        
        // Bar state
        public int IntoxicationLevel { get; set; }
        public DateTime? SoberUpTime { get; set; }
        public decimal BarWinningsToday { get; set; }
        public DateTime? BarWinningsDate { get; set; }

        // Poker stats
        public int PokerHandsPlayed { get; set; }
        public int PokerHandsWon { get; set; }
        public decimal PokerProfit { get; set; }
        public decimal PokerBiggestPot { get; set; }

        // Bank
        public decimal BankDeposit { get; set; }

        // Stock Portfolio
        public StockPortfolioSave? Portfolio { get; set; }
        public decimal DividendIncome { get; set; }
        
        // Auto-production limits
        public List<Guid> AutoProductionRecipes { get; set; } = new();
        public Dictionary<Guid, int> AutoProductionMinReserves { get; set; } = new();
        public Dictionary<Guid, int> AutoProductionMaxStock { get; set; } = new();
        public Dictionary<Guid, int> AutoProductionLevels { get; set; } = new();
        public Dictionary<Guid, int> AutoProductionProgress { get; set; } = new();

        // AI specific
        public bool IsAI { get; set; }
        public GeneticStrategy? Strategy { get; set; }
        public decimal DailyProfit { get; set; }
        public int TotalTrades { get; set; }
        public int ProfitableTrades { get; set; }
    }

    public class MarketItemSave
    {
        public string Name { get; set; } = "";
        public decimal CurrentPrice { get; set; }
        public List<decimal> PriceHistory { get; set; } = new();
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }
    }

    public class InventoryItemSave
    {
        public string ItemName { get; set; } = "";
        public decimal AveragePrice { get; set; }
        public int Quantity { get; set; }
    }

    public class VehicleSave
    {
        public VehicleType Type { get; set; }
        public string Name { get; set; } = "";
        public int CargoCapacity { get; set; }
        public decimal PurchasePrice { get; set; }
    }

    public class PropertySave
    {
        public PropertyType Type { get; set; }
        public string Name { get; set; } = "";
        public decimal PurchasePrice { get; set; }
        public decimal MonthlyRent { get; set; }
        public int GuestCapacity { get; set; }
        public decimal BirthdayGiftBonus { get; set; }
    }

    public class LandSave
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public LandType Type { get; set; }
        public string Name { get; set; } = "";
        public decimal PurchasePrice { get; set; }
        public int MaxWarehouseLevel { get; set; }
    }

    public class WarehouseSave
    {
        public Guid WarehouseId { get; set; }
        public WarehouseType Type { get; set; }
        public string Name { get; set; } = "";
        public int Capacity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal MonthlyMaintenance { get; set; }
    }

    public class LoanSave
    {
        public decimal Amount { get; set; }
        public decimal InterestRate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Penalty { get; set; }
        public bool IsDefaulted { get; set; }
    }

    public class IndustrialFactorySave
    {
        public FactoryType Type { get; set; }
        public string Name { get; set; } = "";
        public decimal PurchasePrice { get; set; }
        public decimal MonthlyMaintenance { get; set; }
        public bool IsOperational { get; set; }
        public decimal EfficiencyMultiplier { get; set; }
        public int ProductionLevel { get; set; }

        // Agriculture harvest cycle state
        public DateTime? CurrentCycleStart { get; set; }
        public bool IsDiseased { get; set; }
    }

    public class StockPortfolioSave
    {
        public Dictionary<string, int> Holdings { get; set; } = new();
        public Dictionary<string, decimal> AvgBuyPrice { get; set; } = new();
        public List<StockOrderSave> PendingOrders { get; set; } = new();
    }

    public class StockOrderSave
    {
        public string Ticker { get; set; } = "";
        public OrderType Type { get; set; }
        public decimal TargetPrice { get; set; }
        public int Quantity { get; set; }
        public bool IsBuy { get; set; }
    }

    public class StockSaveData
    {
        public string Ticker { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public decimal SharePrice { get; set; }
        public decimal DividendYield { get; set; }
        public int TotalShares { get; set; }
        public int AvailableShares { get; set; }
        public List<decimal> PriceHistory { get; set; } = new();
        public string LinkedCommodity { get; set; } = "";
        public decimal CorrelationFactor { get; set; }
    }
}

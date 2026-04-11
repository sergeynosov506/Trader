using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Configuration;
using EconomicGame.Models;


namespace EconomicGame
{
    public class Player
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
        public decimal Money { get; set; } = GameConstants.InitialPlayerMoney;
        public List<InventoryItem> Inventory { get; set; } = new List<InventoryItem>();
        public Vehicle? Vehicle { get; set; } = null;
        public Property? Property { get; set; }
        public Land? Land { get; set; }
        
        // Multi-warehouse system (Trader Path)
        public List<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
        
        // Trading License (Trader Path)
        public int TradingLicenseLevel { get; set; } = 0; // 0 = no license, 1-3 = levels
        public decimal TradeVolume { get; set; } = 0; // Lifetime trade volume for reputation
        
        public List<IndustrialFactory> Factories { get; set; } = new List<IndustrialFactory>();
        public List<Guid> RivalPlayerIds { get; set; } = new List<Guid>();
        public List<Guid> OwnedAIIds { get; set; } = new List<Guid>();
        public bool IsSabotaged { get; set; }
        public DateTime? SabotageEndTime { get; set; }
        public Guid? OwnerId { get; set; }
        public decimal CorporateThreatLevel { get; set; } // 0.0 to 1.0
        public List<Loan> Loans { get; set; } = new List<Loan>();
        public bool IsBankrupt { get; set; }
        public bool IsAI { get; set; }
        public DateTime LastHousingCheck { get; set; } = DateTime.Now;
        public int Reputation { get; set; } = GameConstants.InitialReputation;
        public List<GameEvent> PendingEvents { get; set; } = new List<GameEvent>();
        
        // Bank savings/deposits
        public decimal BankDeposit { get; set; } = 0;
        public DateTime? LastInterestPaid { get; set; }
        
        // Corporate Insurance
        public bool HasInsurance { get; set; }
        public DateTime? InsuranceExpiry { get; set; }
        
        // Monthly financial tracking
        public List<MonthlyReport> MonthlyReports { get; set; } = new List<MonthlyReport>();
        public decimal MonthlyIncome { get; set; } = 0;
        public decimal MonthlyExpenses { get; set; } = 0;
        public int LastReportMonth { get; set; } = 0;
        
        // Bar state
        public int IntoxicationLevel { get; set; } = 0;
        public DateTime? LastBarVisit { get; set; }
        public DateTime? SoberUpTime { get; set; }
        
        // Production system
        public List<Guid> AutoProductionRecipes { get; set; } = new List<Guid>();

        // Stock Market Portfolio (Investor Path)
        public StockPortfolio Portfolio { get; set; } = new StockPortfolio();
        public decimal DividendIncome { get; set; } = 0; // Lifetime dividend income

        // Collective Intelligence / Digital Soul
        public GeneticStrategy Strategy { get; set; } = new GeneticStrategy();
        public decimal DailyProfit { get; set; } = 0;
        public int TotalTrades { get; set; } = 0;
        public int ProfitableTrades { get; set; } = 0;
        public decimal SuccessScore => TotalTrades == 0 ? 0 : (decimal)ProfitableTrades / TotalTrades;

        // Calculated properties
        public int TotalCargoCapacity => 
            (Vehicle?.CargoCapacity ?? 0) + Warehouses.Sum(w => w.Capacity);
        
        public int CurrentCargoUsed => 
            Inventory.Sum(i => i.Quantity);
        
        public int AvailableCargoSpace => 
            TotalCargoCapacity - CurrentCargoUsed;

        /// <summary>
        /// Max number of warehouses based on trading license level
        /// </summary>
        public int MaxWarehouses => TradingLicenseLevel switch
        {
            0 => GameConstants.MaxWarehousesLevel0,
            1 => GameConstants.MaxWarehousesLevel1,
            2 => GameConstants.MaxWarehousesLevel2,
            3 => GameConstants.MaxWarehousesLevel3,
            _ => GameConstants.MaxWarehousesLevel0
        };

        /// <summary>
        /// Total net worth including all assets
        /// </summary>
        public decimal NetWorth =>
            Money + BankDeposit +
            Inventory.Sum(i => i.AveragePrice * i.Quantity) +
            Warehouses.Sum(w => w.PurchasePrice * 0.5m) +
            (Vehicle?.PurchasePrice ?? 0) * 0.5m +
            (Land?.PurchasePrice ?? 0) * 0.7m +
            (Property?.PurchasePrice ?? 0) * 0.7m +
            Factories.Sum(f => f.PurchasePrice * 0.5m);

        // Backward compatibility — returns first warehouse or null
        [System.Text.Json.Serialization.JsonIgnore]
        public Warehouse? Warehouse
        {
            get => Warehouses.FirstOrDefault();
            set
            {
                if (value == null)
                {
                    if (Warehouses.Any()) Warehouses.RemoveAt(0);
                }
                else
                {
                    if (Warehouses.Any()) Warehouses[0] = value;
                    else Warehouses.Add(value);
                }
            }
        }
    }

    public class InventoryItem
    {
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public required string ItemName { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal AveragePrice { get; set; }
        public int Quantity { get; set; }
    }

    #region Vehicle System

    public enum VehicleType
    {
        None,
        BasicCar,      // 🚗 Легковая - 50 capacity
        Van,           // 🚐 Фургон - 150 capacity
        Truck,         // 🚛 Грузовик - 500 capacity
        SemiTruck      // 🚚 Фура - 2000 capacity
    }

    public class Vehicle
    {
        public required VehicleType Type { get; set; }
        public required string Name { get; set; }
        public string Emoji { get; set; } = "🚗";
        public int CargoCapacity { get; set; }
        public decimal PurchasePrice { get; set; }
        public bool IsOperational { get; set; } = true;
        public decimal RepairCost { get; set; }
        public DateTime PurchaseDate { get; set; }
    }

    #endregion

    #region Land System

    public enum LandType
    {
        None,
        SmallPlot,     // 🏞️ Маленький участок
        MediumPlot,    // 🏞️ Средний участок
        LargePlot      // 🏞️ Большой участок
    }

    public class Land
    {
        public required LandType Type { get; set; }
        public required string Name { get; set; }
        public string Emoji { get; set; } = "🏞️";
        public decimal PurchasePrice { get; set; }
        public DateTime PurchaseDate { get; set; }
        public int MaxWarehouseLevel { get; set; }  // Which warehouses can be built
    }

    #endregion

    #region Warehouse System

    public enum WarehouseType
    {
        None,
        MiniWarehouse,     // 📦 Мини-склад - 1000 capacity
        Warehouse,         // 🏭 Склад - 5000 capacity
        LargeWarehouse,    // 🏗️ Большой склад - 25000 capacity
        IndustrialComplex, // 🏭 Промышленный комплекс - 50000 capacity
        TradeHub           // 🌐 Торговый хаб - 100000 capacity
    }

    public class Warehouse
    {
        public Guid WarehouseId { get; set; } = Guid.NewGuid();
        public required WarehouseType Type { get; set; }
        public required string Name { get; set; }
        public string Emoji { get; set; } = "📦";
        public int Capacity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal MonthlyMaintenance { get; set; }
        public DateTime PurchaseDate { get; set; }
        public DateTime LastMaintenancePaid { get; set; }
    }

    #endregion

    #region Housing System

    public enum PropertyType
    {
        None,
        SmallRoom,
        Apartment,
        House,
        Mansion
    }

    public class Property
    {
        public required PropertyType Type { get; set; }
        public required string Name { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal MonthlyRent { get; set; }
        public DateTime PurchaseDate { get; set; }
        public DateTime LastRentPaid { get; set; }
        public int GuestCapacity { get; set; }
        public decimal BirthdayGiftBonus { get; set; }
    }

    #endregion

    #region Loan System

    public class Loan
    {
        public Guid LoanId { get; set; } = Guid.NewGuid();
        public decimal Amount { get; set; }
        public decimal InterestRate { get; set; } = GameConstants.DefaultInterestRate;
        public DateTime DueDate { get; set; }
        public decimal Penalty { get; set; }
        public bool IsDefaulted { get; set; }
    }

    #endregion

    #region Stock Portfolio

    public class StockPortfolio
    {
        /// <summary>
        /// Holdings: Ticker -> number of shares
        /// </summary>
        public Dictionary<string, int> Holdings { get; set; } = new Dictionary<string, int>();
        
        /// <summary>
        /// Average buy price per ticker
        /// </summary>
        public Dictionary<string, decimal> AvgBuyPrice { get; set; } = new Dictionary<string, decimal>();
        
        /// <summary>
        /// Pending limit/stop-loss orders
        /// </summary>
        public List<StockOrder> PendingOrders { get; set; } = new List<StockOrder>();
    }

    public enum OrderType
    {
        Market,
        Limit,
        StopLoss
    }

    public class StockOrder
    {
        public Guid OrderId { get; set; } = Guid.NewGuid();
        public required string Ticker { get; set; }
        public OrderType Type { get; set; }
        public decimal TargetPrice { get; set; }
        public int Quantity { get; set; }
        public bool IsBuy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    #endregion

    #region Monthly Financial Report

    public class MonthlyReport
    {
        public int Month { get; set; }
        public int Day { get; set; }
        public decimal StartingBalance { get; set; }
        public decimal EndingBalance { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal InterestEarned { get; set; }
        public decimal LoanPayments { get; set; }
        public decimal TradingProfit { get; set; }
        public decimal ProductionProfit { get; set; }
        public decimal DividendIncome { get; set; }
        public decimal NetChange => EndingBalance - StartingBalance;
    }

    #endregion
}

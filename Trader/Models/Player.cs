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
        public List<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
        public List<Property> Properties { get; set; } = new List<Property>();
        public List<Land> Lands { get; set; } = new List<Land>();

        [System.Text.Json.Serialization.JsonIgnore]
        public Land? Land
        {
            get => Lands.FirstOrDefault();
            set
            {
                if (value == null)
                {
                    if (Lands.Any()) Lands.RemoveAt(0);
                }
                else
                {
                    if (Lands.Any()) Lands[0] = value;
                    else Lands.Add(value);
                }
            }
        }
        
        // Multi-warehouse system (Trader Path)
        public List<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
        
        // Trading License (Trader Path)
        public int TradingLicenseLevel { get; set; } = 0; // 0 = no license, 1-3 = levels
        public decimal TradeVolume { get; set; } = 0; // Lifetime trade volume for reputation
        
        public List<IndustrialFactory> Factories { get; set; } = new List<IndustrialFactory>();
        public List<Guid> RivalPlayerIds { get; set; } = new List<Guid>();
        public List<Guid> OwnedAIIds { get; set; } = new List<Guid>();
        private bool _isSabotaged;
        public bool IsSabotaged
        {
            get => _isSabotaged && TradingLicenseLevel == 0;
            set => _isSabotaged = value;
        }
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

        // Bar gambling limits (economy rebalance): net winnings per game day
        public decimal BarWinningsToday { get; set; } = 0m;
        public DateTime? BarWinningsDate { get; set; }

        // Poker career stats (player and bots alike)
        public int PokerHandsPlayed { get; set; }
        public int PokerHandsWon { get; set; }
        public decimal PokerProfit { get; set; }
        public decimal PokerBiggestPot { get; set; }
        
        // Production system
        public List<Guid> AutoProductionRecipes { get; set; } = new List<Guid>();
        public Dictionary<Guid, int> AutoProductionMinReserves { get; set; } = new Dictionary<Guid, int>();
        public Dictionary<Guid, int> AutoProductionMaxStock { get; set; } = new Dictionary<Guid, int>();
        public Dictionary<Guid, int> AutoProductionLevels { get; set; } = new Dictionary<Guid, int>();
        // Ticks elapsed in the current production cycle per recipe (recipes now take real time)
        public Dictionary<Guid, int> AutoProductionProgress { get; set; } = new Dictionary<Guid, int>();

        // Stock Market Portfolio (Investor Path)
        public StockPortfolio Portfolio { get; set; } = new StockPortfolio();
        public decimal DividendIncome { get; set; } = 0; // Lifetime dividend income

        // Active scenario / challenge (null = freeplay)
        public string? ActiveScenarioId { get; set; }
        public DateTime? ScenarioStartTime { get; set; }
        public ScenarioStatus ScenarioStatus { get; set; } = ScenarioStatus.None;

        // Scenario race vs bots: rival bots picked at scenario start.
        // A rival "wins the race" by gaining as much equity as the scenario requires
        // from the player — then the scenario is lost. (Transient, like all scenario state.)
        public List<Guid> ScenarioRivalIds { get; set; } = new List<Guid>();
        public Dictionary<Guid, decimal> ScenarioRivalStartEquity { get; set; } = new Dictionary<Guid, decimal>();
        public decimal ScenarioStartEquity { get; set; }
        public string? ScenarioRaceWinner { get; set; }

        // Collective Intelligence / Digital Soul
        public GeneticStrategy Strategy { get; set; } = new GeneticStrategy();
        public decimal DailyProfit { get; set; } = 0;
        public int TotalTrades { get; set; } = 0;
        public int ProfitableTrades { get; set; } = 0;
        public decimal SuccessScore => TotalTrades == 0 ? 0 : (decimal)ProfitableTrades / TotalTrades;

        // Calculated properties
        public int TotalCargoCapacity => 
            Vehicles.Sum(v => v.CargoCapacity) + Warehouses.Sum(w => w.Capacity);
        
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
        /// Max number of land plots based on trading license level
        /// </summary>
        public int MaxLandPlots => TradingLicenseLevel switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            3 => 5,
            _ => 1
        };

        /// <summary>
        /// Total net worth including all assets. Inventory is valued at cost basis
        /// here because this property does not have access to the live market.
        /// For a mark-to-market figure, use <see cref="ComputeMarketNetWorth"/> instead.
        /// Thread-safe to prevent collection modification exceptions during background ticks.
        /// </summary>
        public decimal NetWorth
        {
            get
            {
                try
                {
                    decimal invVal = 0m, whVal = 0m, vehVal = 0m, lndVal = 0m, propVal = 0m, facVal = 0m;
                    lock (Inventory) invVal = Inventory.Sum(i => i.AveragePrice * i.Quantity);
                    lock (Warehouses) whVal = Warehouses.Sum(w => w.PurchasePrice * 0.5m);
                    lock (Vehicles) vehVal = Vehicles.Sum(v => v.PurchasePrice * 0.5m);
                    lock (Lands) lndVal = Lands.Sum(l => l.PurchasePrice * 0.7m);
                    lock (Properties) propVal = Properties.Sum(p => p.PurchasePrice * 0.7m);
                    lock (Factories) facVal = Factories.Sum(f => f.PurchasePrice * 0.5m);

                    return Money + BankDeposit + invVal + whVal + vehVal + lndVal + propVal + facVal;
                }
                catch (InvalidOperationException)
                {
                    return Money + BankDeposit;
                }
            }
        }

        /// <summary>
        /// Returns net worth valuing inventory at current market prices (mark-to-market).
        /// Falls back to cost basis for items missing from the market snapshot.
        /// Thread-safe against concurrent collection modifications.
        /// </summary>
        public decimal ComputeMarketNetWorth(IEnumerable<MarketItem> marketItems)
        {
            try
            {
                decimal inventoryValue = 0m;
                InventoryItem[] invSnapshot;
                lock (Inventory) invSnapshot = Inventory.ToArray();

                var mSnapshot = marketItems.ToList();
                foreach (var item in invSnapshot)
                {
                    var market = mSnapshot.FirstOrDefault(m => m.Name == item.ItemName);
                    var unit = market?.CurrentPrice ?? item.AveragePrice;
                    inventoryValue += unit * item.Quantity;
                }

                decimal whVal = 0m, vehVal = 0m, lndVal = 0m, propVal = 0m, facVal = 0m;
                lock (Warehouses) whVal = Warehouses.Sum(w => w.PurchasePrice * 0.5m);
                lock (Vehicles) vehVal = Vehicles.Sum(v => v.PurchasePrice * 0.5m);
                lock (Lands) lndVal = Lands.Sum(l => l.PurchasePrice * 0.7m);
                lock (Properties) propVal = Properties.Sum(p => p.PurchasePrice * 0.7m);
                lock (Factories) facVal = Factories.Sum(f => f.PurchasePrice * 0.5m);

                return Money + BankDeposit + inventoryValue + whVal + vehVal + lndVal + propVal + facVal;
            }
            catch (InvalidOperationException)
            {
                return Money + BankDeposit;
            }
        }

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

        // Backward compatibility — returns first vehicle or null
        [System.Text.Json.Serialization.JsonIgnore]
        public Vehicle? Vehicle
        {
            get => Vehicles.FirstOrDefault();
            set
            {
                if (value == null)
                {
                    if (Vehicles.Any()) Vehicles.RemoveAt(0);
                }
                else
                {
                    if (Vehicles.Any()) Vehicles[0] = value;
                    else Vehicles.Add(value);
                }
            }
        }

        // Backward compatibility — returns first property or null
        [System.Text.Json.Serialization.JsonIgnore]
        public Property? Property
        {
            get => Properties.FirstOrDefault();
            set
            {
                if (value == null)
                {
                    if (Properties.Any()) Properties.RemoveAt(0);
                }
                else
                {
                    if (Properties.Any()) Properties[0] = value;
                    else Properties.Add(value);
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
        public Guid Id { get; set; } = Guid.NewGuid();
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

    /// <summary>
    /// Flavor types for random bar encounters returned by TryMeetSomeone.
    /// UI uses this to pick the right translation and show context-aware actions
    /// (e.g. "Buy him a drink" button for DrinkTogether).
    /// </summary>
    public enum BarEncounterType
    {
        DrinkTogether,   // Some guy sits down and wants to drink together
        BarmanStories,   // Barman tells market stories
        FellowTrader,    // A fellow trader spotted in the corner
        LoudCrowd        // Loud group discussing deals nearby
    }

    /// <summary>
    /// Scam flavor outcomes when buying a drink for the stranger backfires.
    /// UI localizes via Loc["bar.scam_*"].
    /// </summary>
    public enum BarScamType
    {
        DrinkAndLeave,   // He downs the drink and just walks out
        BoringStory,     // He rambles on about something useless for an hour
        BathroomExit,    // "Be right back" — never comes back
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

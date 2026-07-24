using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Configuration;
using EconomicGame.Extensions;
using EconomicGame.Models;
using Microsoft.Extensions.Configuration;

namespace EconomicGame.Services
{
    public class GameEngine
    {
        private static readonly Random _random = Random.Shared;
        private readonly Market _market;
        private readonly StockExchange _exchange;
        public StockExchange StockExchange => _exchange;
        private readonly Bank _bank;
        private readonly List<News> _news;
        private readonly IGameBroadcaster _broadcaster;
        private readonly PlayerService _playerService;
        private readonly SyncEngine _syncEngine;
        private readonly CorporateRivalryService _rivalryService;
        private readonly CorporateActionService _actionService;
        private readonly InsuranceService _insuranceService;
        private readonly StockMarketService _stockMarketService;
        private readonly ScenarioService _scenarioService;

        /// <summary>
        /// Scenarios defined in appsettings.json -> "Scenarios". Immutable at runtime.
        /// </summary>
        public IReadOnlyList<Scenario> Scenarios { get; }

        public ScenarioService ScenarioService => _scenarioService;

        /// <summary>
        /// Start a scenario WITH bot rivals (bot-competitors feature): passes the AI
        /// population and market snapshot so the race can be set up fairly.
        /// </summary>
        public bool StartScenarioRace(Player player, string scenarioId)
        {
            var ok = _scenarioService.StartScenario(player, scenarioId, CurrentTime, _playerService.GetAllPlayers(), _exchange.Items);
            if (ok && player.ScenarioRivalIds.Any())
            {
                var rivalNames = _playerService.GetAllPlayers()
                    .Where(p => player.ScenarioRivalIds.Contains(p.Id))
                    .Select(p => p.Name);
                LogActivity($"🏁 {player.Name} начал сценарий-гонку! Соперники: {string.Join(", ", rivalNames)}");
            }
            return ok;
        }

        // Thread-safe activity log
        private readonly object _logLock = new();
        private readonly List<string> _activityLog = new();
        public IReadOnlyList<string> MarketActivityLog 
        {
            get
            {
                lock (_logLock)
                {
                    return _activityLog.ToList();
                }
            }
        }

        public event Action? OnStateChanged;

        public StockMarketService StockMarket => _stockMarketService;

        public GameEngine(IGameBroadcaster broadcaster, IConfiguration configuration, PlayerService playerService, SyncEngine syncEngine, CorporateRivalryService rivalryService, CorporateActionService actionService, InsuranceService insuranceService, StockMarketService stockMarketService, ScenarioService scenarioService)
        {
            _market = new Market();

            var items = configuration.GetSection("MarketItems").Get<List<MarketItem>>() ?? new List<MarketItem>();
            _exchange = new StockExchange(items);

            _bank = new Bank();
            _news = new List<News>();
            _broadcaster = broadcaster;
            _playerService = playerService;
            _syncEngine = syncEngine;
            _rivalryService = rivalryService;
            _actionService = actionService;
            _insuranceService = insuranceService;
            _stockMarketService = stockMarketService;
            _scenarioService = scenarioService;

            Scenarios = configuration.GetSection("Scenarios").Get<List<Scenario>>() ?? new List<Scenario>();
            _scenarioService.LoadScenarios(Scenarios, _bank);
        }

        public DateTime CurrentTime { get; private set; } = DateTime.Today.AddHours(8); // Start at 8 AM

        public void SetCurrentTime(DateTime time)
        {
            CurrentTime = time;
        }

        /// <summary>
        /// Public method to trigger UI refresh (used after save/load)
        /// </summary>
        public void TriggerStateChanged()
        {
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// True while the human player has the bar (or poker room) open —
        /// game time flows slower so an evening at the bar actually lasts an evening.
        /// Set by BarView/PokerView on enter/leave.
        /// </summary>
        public bool PlayerInBarZone { get; set; }

        public void UpdateGameState()
        {
            if (_playerService.GetCurrentPlayer() == null) return;

            var previousTime = CurrentTime;
            // Time dilation: the world slows down while you're at the bar
            CurrentTime = CurrentTime.AddMinutes(PlayerInBarZone ? GameConstants.BarTickMinutes : GameConstants.NormalTickMinutes);

            // Night Cycle / Collective Intelligence Sync
            if (previousTime.Hour == 23 && CurrentTime.Hour == 0)
            {
                LogActivity("Collective Consciousness Sync Initiated...");
                _syncEngine.PerformNightlySync();
                RefillBarBankroll();

                // Living in your car has consequences — every night without a home
                foreach (var p in _playerService.GetAllPlayers())
                {
                    ProcessHomelessNight(p);
                }
            }

            _exchange.UpdatePrices();

            // Stock Market updates — ensure service uses game time for market-hours checks
            _stockMarketService.CurrentGameTime = CurrentTime;
            _stockMarketService.UpdateStockPrices(_exchange.Items);
            _stockMarketService.ProcessPendingOrders();
            _stockMarketService.PayDividends(CurrentTime);
            
            // Process pending news effects (rumors that should now trigger)
            ProcessPendingNewsEffects();
            GenerateNews();
            GenerateDisaster();
            
            // Generate random events
            GenerateRandomEvents();
            ProcessExpiredEvents();
            
            // Banking Updates — global, run once per tick
            _bank.UpdateInterestRate();

            // Corporate Rivalry updates — global, don't run every tick
            if (_random.NextDouble() < 0.05) // 5% chance per tick to recalculate rivals
            {
                _rivalryService.UpdateRivals();
            }

            // Shadow Operations maintenance — global, run once per tick
            foreach (var p in _playerService.GetAllPlayers())
            {
                if (p.IsSabotaged && p.SabotageEndTime.HasValue && CurrentTime >= p.SabotageEndTime.Value)
                {
                    p.IsSabotaged = false;
                    p.SabotageEndTime = null;
                }
            }

            // Subsidiary payouts — global, don't run every tick
            if (_random.NextDouble() < 0.02) // 2% chance per tick for subsidiary payouts
            {
                _actionService.ProcessSubsidaryPayouts();
            }

            // Human-player-only services (don't need to run N times for N AI players)
            _insuranceService.UpdateInsuranceStatus(CurrentTime);

            // Per-player updates — run once per player
            var currentDay = (int)(CurrentTime - DateTime.Today.AddHours(8)).TotalDays;
            foreach (var player in _playerService.GetAllPlayers())
            {
                _bank.CheckLoans(player, CurrentTime);
                _bank.PayDepositInterest(player, CurrentTime);
                ProcessIntoxication(player);
                ProcessRent(player);
                ProcessWarehouseMaintenance(player);
                ProcessFactoryMaintenance(player);
                ProcessOverloadPenalty(player);
                ProcessAutoProduction(player);
                ProcessAgriculturalGrowth(player);

                // Evaluate any active scenario — only for human players,
                // AI don't participate in challenges.
                if (!player.IsAI && player.ScenarioStatus == ScenarioStatus.Active)
                {
                    var prev = player.ScenarioStatus;
                    var next = _scenarioService.EvaluateScenario(player, CurrentTime, _exchange.Items, _playerService.GetAllPlayers());
                    if (next != prev)
                    {
                        var s = _scenarioService.GetActiveScenario(player);
                        var name = s?.Id ?? "?";
                        if (next == ScenarioStatus.Lost && player.ScenarioRaceWinner != null)
                        {
                            LogActivity($"🏁 {player.ScenarioRaceWinner} выиграл гонку в сценарии '{name}' — {player.Name} опоздал!");
                        }
                        else
                        {
                            LogActivity(next == ScenarioStatus.Won
                                ? $"Scenario '{name}' WON by {player.Name}"
                                : $"Scenario '{name}' LOST by {player.Name}");
                        }
                    }
                }

                // Generate monthly report every N game days
                if (currentDay > 0 && currentDay % GameConstants.MonthlyReportDays == 0 && player.LastReportMonth != currentDay)
                {
                    GenerateMonthlyReport(player, currentDay);
                }
            }

            // Notify local subscribers (Server-side Blazor components) — once per tick
            OnStateChanged?.Invoke();

            // Notify external clients (if any) — no-op on hosts without SignalR (WASM)
            _broadcaster.BroadcastPrices(_exchange.Items);
            _broadcaster.BroadcastNews(_news.LastOrDefault());
        }

        #region Logging

        public void LogActivity(string message)
        {
            lock (_logLock)
            {
                _activityLog.Insert(0, $"[{CurrentTime.ToShortTimeString()}] {message}");
                if (_activityLog.Count > GameConstants.MaxActivityLogEntries)
                    _activityLog.RemoveAt(_activityLog.Count - 1);
            }
        }

        #endregion

        #region Financial Reporting

        private void GenerateMonthlyReport(Player player, int currentDay)
        {
            var report = new MonthlyReport
            {
                Month = currentDay / GameConstants.MonthlyReportDays,
                Day = currentDay,
                StartingBalance = player.MonthlyReports.Any() 
                    ? player.MonthlyReports.Last().EndingBalance 
                    : GameConstants.InitialPlayerMoney,
                EndingBalance = player.Money + player.BankDeposit,
                TotalIncome = player.MonthlyIncome,
                TotalExpenses = player.MonthlyExpenses,
                InterestEarned = player.BankDeposit > 0 ? player.BankDeposit * _bank.GetDepositInterestRate() / 12 : 0,
                LoanPayments = player.Loans.Sum(l => l.Amount),
            };
            
            player.MonthlyReports.Add(report);
            player.LastReportMonth = currentDay;
            
            // Reset monthly tracking
            player.MonthlyIncome = 0;
            player.MonthlyExpenses = 0;
            
            // Keep only last N reports
            while (player.MonthlyReports.Count > GameConstants.MaxMonthlyReports)
            {
                player.MonthlyReports.RemoveAt(0);
            }
        }

        #endregion

        #region Logistics System

        public static readonly List<(VehicleType Type, string Name, string Emoji, decimal Price, int Capacity)> AvailableVehicles = new()
        {
            (VehicleType.BasicCar, "Легковая", "🚗", GameConstants.BasicCarPrice, GameConstants.BasicCarCapacity),
            (VehicleType.Van, "Фургон", "🚐", GameConstants.VanPrice, GameConstants.VanCapacity),
            (VehicleType.Truck, "Грузовик", "🚛", GameConstants.TruckPrice, GameConstants.TruckCapacity),
            (VehicleType.SemiTruck, "Фура", "🚚", GameConstants.SemiTruckPrice, GameConstants.SemiTruckCapacity)
        };

        public static readonly List<(LandType Type, string Name, string Emoji, decimal Price, int MaxWarehouse)> AvailableLand = new()
        {
            (LandType.SmallPlot, "Маленький участок", "🏞️", GameConstants.SmallPlotPrice, GameConstants.SmallPlotMaxWarehouse),
            (LandType.MediumPlot, "Средний участок", "🏞️", GameConstants.MediumPlotPrice, GameConstants.MediumPlotMaxWarehouse),
            (LandType.LargePlot, "Большой участок", "🏞️", GameConstants.LargePlotPrice, GameConstants.LargePlotMaxWarehouse)
        };

        public static readonly List<(WarehouseType Type, string Name, string Emoji, decimal Price, int Capacity, decimal Maintenance, int RequiredLandLevel)> AvailableWarehouses = new()
        {
            (WarehouseType.MiniWarehouse, "Мини-склад", "📦", GameConstants.MiniWarehousePrice, GameConstants.MiniWarehouseCapacity, GameConstants.MiniWarehouseMaintenance, 1),
            (WarehouseType.Warehouse, "Склад", "🏭", GameConstants.WarehousePrice, GameConstants.WarehouseCapacity, GameConstants.WarehouseMaintenance, 2),
            (WarehouseType.LargeWarehouse, "Большой склад", "🏗️", GameConstants.LargeWarehousePrice, GameConstants.LargeWarehouseCapacity, GameConstants.LargeWarehouseMaintenance, 3),
            (WarehouseType.IndustrialComplex, "Промышленный комплекс", "🏭", GameConstants.IndustrialComplexPrice, GameConstants.IndustrialComplexCapacity, GameConstants.IndustrialComplexMaintenance, 3),
            (WarehouseType.TradeHub, "Торговый хаб", "🌐", GameConstants.TradeHubPrice, GameConstants.TradeHubCapacity, GameConstants.TradeHubMaintenance, 3)
        };

        public static readonly List<(FactoryType Type, string Name, string Emoji, decimal Price, decimal Maintenance, string TargetRecipe)> AvailableFactories = new()
        {
            (FactoryType.SugarRefinery, "Сахарный завод", "🍬", GameConstants.SugarRefineryPrice, GameConstants.SugarRefineryMaintenance, "Сахар"),
            (FactoryType.SteelMill, "Сталелитейный цех", "🏗️", GameConstants.SteelMillPrice, GameConstants.SteelMillMaintenance, "Оборудование"),
            (FactoryType.ChemicalPlant, "Химический завод", "🧪", GameConstants.ChemicalPlantPrice, GameConstants.ChemicalPlantMaintenance, "Химикаты"),
            (FactoryType.TextileMill, "Текстильная фабрика", "🧵", GameConstants.TextileMillPrice, GameConstants.TextileMillMaintenance, "Текстиль"),
            (FactoryType.PharmLab, "Фармацевтическая лаборатория", "💊", GameConstants.PharmLabPrice, GameConstants.PharmLabMaintenance, "Фармацевтика"),
            
            (FactoryType.SugarCanePlantation, "Плантация тростника", "🎋", 80000m, 1200m, "Сахарный тростник"),
            (FactoryType.CoffeePlantation, "Кофейная плантация", "☕", 120000m, 1800m, "Кофе-бобы"),
            (FactoryType.WheatFarm, "Пшеничная ферма", "🌾", 40000m, 600m, "Пшеница")
        };

        public string BuyVehicle(Player player, VehicleType vehicleType)
        {
            if (player == null) return "Игрок не найден!";

            var vehicleInfo = AvailableVehicles.FirstOrDefault(v => v.Type == vehicleType);
            if (vehicleInfo == default) return "Такого транспорта нет!";

            if (player.Money < vehicleInfo.Price)
                return $"Не хватает денег! Нужно {vehicleInfo.Price:C}, у тебя {player.Money:C}";

            player.Money -= vehicleInfo.Price;
            var newVehicle = new Vehicle
            {
                Type = vehicleType,
                Name = vehicleInfo.Name,
                Emoji = vehicleInfo.Emoji,
                CargoCapacity = vehicleInfo.Capacity,
                PurchasePrice = vehicleInfo.Price,
                IsOperational = true,
                PurchaseDate = CurrentTime
            };
            player.Vehicles.Add(newVehicle);

            LogActivity($"{player.Name} купил {vehicleInfo.Emoji} {vehicleInfo.Name} за {vehicleInfo.Price:C}");
            OnStateChanged?.Invoke();
            return $"Поздравляем! {vehicleInfo.Emoji} {vehicleInfo.Name} теперь твой! Вместимость: {vehicleInfo.Capacity} ед.";
        }

        public string SellVehicle(Player player, Vehicle vehicle)
        {
            if (player == null) return "Игрок не найден!";
            if (vehicle == null) return "Транспорт не найден!";
            if (!player.Vehicles.Contains(vehicle)) return "У тебя нет этого транспорта!";

            var currentCargo = player.Inventory.Sum(i => i.Quantity);
            var newCapacity = player.TotalCargoCapacity - vehicle.CargoCapacity;
            if (currentCargo > newCapacity)
            {
                return $"Нельзя продать этот транспорт! Твой груз ({currentCargo} ед.) превысит новую общую вместимость ({newCapacity} ед.). Сначала продай товары или арендуй склад.";
            }

            var sellPrice = vehicle.PurchasePrice * 0.6m;
            var vehicleName = $"{vehicle.Emoji} {vehicle.Name}";

            player.Money += sellPrice;
            player.Vehicles.Remove(vehicle);

            LogActivity($"{player.Name} продал {vehicleName} за {sellPrice:C}");
            OnStateChanged?.Invoke();
            return $"Продал {vehicleName} за {sellPrice:C} (60% от цены покупки)";
        }

        public string SellVehicle(Player player)
        {
            if (player == null) return "Игрок не найден!";
            var vehicle = player.Vehicles.FirstOrDefault();
            if (vehicle == null) return "У тебя нет транспорта!";
            return SellVehicle(player, vehicle);
        }

        public string BuyLand(Player player, LandType landType)
        {
            if (player == null) return "Игрок не найден!";
            if (player.Lands.Count >= player.MaxLandPlots)
                return $"Достигнут лимит земельных участков ({player.MaxLandPlots})! Купи торговую лицензию для расширения.";

            var landInfo = AvailableLand.FirstOrDefault(l => l.Type == landType);
            if (landInfo == default) return "Такого участка нет!";

            if (player.Money < landInfo.Price)
                return $"Не хватает денег! Нужно {landInfo.Price:C}";

            player.Money -= landInfo.Price;
            var newLand = new Land
            {
                Id = Guid.NewGuid(),
                Type = landType,
                Name = landInfo.Name,
                Emoji = landInfo.Emoji,
                PurchasePrice = landInfo.Price,
                PurchaseDate = CurrentTime,
                MaxWarehouseLevel = landInfo.MaxWarehouse
            };
            player.Lands.Add(newLand);

            LogActivity($"{player.Name} купил {landInfo.Emoji} {landInfo.Name} за {landInfo.Price:C}");
            OnStateChanged?.Invoke();
            return $"Поздравляем! {landInfo.Emoji} {landInfo.Name} теперь твой!";
        }

        public string SellLand(Player player)
        {
            if (player == null) return "Игрок не найден!";
            var land = player.Lands.FirstOrDefault();
            if (land == null) return "У тебя нет земельного участка!";
            return SellLand(player, land.Id);
        }

        public string SellLand(Player player, Guid landId)
        {
            if (player == null) return "Игрок не найден!";
            var land = player.Lands.FirstOrDefault(l => l.Id == landId);
            if (land == null) return "У тебя нет такого земельного участка!";

            // Smart validation: Remaining plots must support all existing warehouses
            var remainingLands = player.Lands.Where(l => l.Id != landId).ToList();
            
            // If we have warehouses but no lands left
            if (player.Warehouses.Any() && !remainingLands.Any())
                return "Нельзя продать последний участок! Сначала продай все склады.";

            // If we have factories but no lands left
            if (player.Factories.Any() && !remainingLands.Any())
                return "Нельзя продать последний участок! Сначала продай все заводы.";

            foreach (var wh in player.Warehouses)
            {
                var whInfo = AvailableWarehouses.FirstOrDefault(w => w.Type == wh.Type);
                var reqLevel = whInfo != default ? whInfo.RequiredLandLevel : 1;
                if (!remainingLands.Any(l => l.MaxWarehouseLevel >= reqLevel))
                {
                    return $"Нельзя продать этот участок! Твой склад ({wh.Emoji} {wh.Name}) требует участок уровня {reqLevel}+, который не поддерживается оставшимися участками.";
                }
            }

            var sellPrice = land.PurchasePrice * 0.7m;
            var landName = $"{land.Emoji} {land.Name}";

            player.Money += sellPrice;
            player.Lands.Remove(land);

            LogActivity($"{player.Name} продал {landName} за {sellPrice:C}");
            OnStateChanged?.Invoke();
            return $"Продал {landName} за {sellPrice:C}";
        }

        public string BuyWarehouse(Player player, WarehouseType warehouseType)
        {
            if (player == null) return "Игрок не найден!";
            if (!player.Lands.Any()) return "Сначала купи земельный участок!";
            
            // Check warehouse limit based on trading license
            if (player.Warehouses.Count >= player.MaxWarehouses)
                return $"Достигнут лимит складов ({player.MaxWarehouses})! Купи торговую лицензию для расширения.";

            var warehouseInfo = AvailableWarehouses.FirstOrDefault(w => w.Type == warehouseType);
            if (warehouseInfo == default) return "Такого склада нет!";

            if (!player.Lands.Any(l => l.MaxWarehouseLevel >= warehouseInfo.RequiredLandLevel))
                return $"Твои участки слишком малы для этого склада! Нужен участок уровня {warehouseInfo.RequiredLandLevel}+";

            if (player.Money < warehouseInfo.Price)
                return $"Не хватает денег! Нужно {warehouseInfo.Price:C}";

            player.Money -= warehouseInfo.Price;
            player.Warehouses.Add(new Warehouse
            {
                Type = warehouseType,
                Name = warehouseInfo.Name,
                Emoji = warehouseInfo.Emoji,
                Capacity = warehouseInfo.Capacity,
                PurchasePrice = warehouseInfo.Price,
                MonthlyMaintenance = warehouseInfo.Maintenance,
                PurchaseDate = CurrentTime,
                LastMaintenancePaid = CurrentTime
            });

            LogActivity($"{player.Name} построил {warehouseInfo.Emoji} {warehouseInfo.Name} за {warehouseInfo.Price:C}");
            OnStateChanged?.Invoke();
            return $"Построен {warehouseInfo.Emoji} {warehouseInfo.Name}! Вместимость: +{warehouseInfo.Capacity} ед. (Складов: {player.Warehouses.Count}/{player.MaxWarehouses})";
        }

        public string BuyFactory(Player player, FactoryType factoryType)
        {
            if (player == null) return "Игрок не найден!";
            if (!player.Lands.Any()) return "Сначала купи земельный участок!";
            
            var factoryInfo = AvailableFactories.FirstOrDefault(f => f.Type == factoryType);
            if (factoryInfo == default) return "Такого завода нет!";

            if (player.Money < factoryInfo.Price)
                return $"Не хватает денег! Нужно {factoryInfo.Price:C}";

            player.Money -= factoryInfo.Price;
            player.Factories.Add(new IndustrialFactory
            {
                Type = factoryType,
                Name = factoryInfo.Name,
                Emoji = factoryInfo.Emoji,
                PurchasePrice = factoryInfo.Price,
                MonthlyMaintenance = factoryInfo.Maintenance,
                PurchaseDate = CurrentTime,
                LastMaintenancePaid = CurrentTime,
                EfficiencyMultiplier = GameConstants.ProductionEfficiencyBoost
            });

            LogActivity($"{player.Name} построил {factoryInfo.Emoji} {factoryInfo.Name} за {factoryInfo.Price:C}");
            OnStateChanged?.Invoke();
            return $"Построен {factoryInfo.Emoji} {factoryInfo.Name}! Эффективность производства: +100%";
        }

        public string SellWarehouse(Player player, Guid? warehouseId = null)
        {
            if (player == null || !player.Warehouses.Any()) return "У тебя нет склада!";

            var warehouse = warehouseId.HasValue 
                ? player.Warehouses.FirstOrDefault(w => w.WarehouseId == warehouseId.Value)
                : player.Warehouses.Last();
                
            if (warehouse == null) return "Склад не найден!";

            // Check if remaining capacity would be enough
            var remainingCapacity = player.Vehicles.Sum(v => v.CargoCapacity) + player.Warehouses.Where(w => w.WarehouseId != warehouse.WarehouseId).Sum(w => w.Capacity);
            if (player.CurrentCargoUsed > remainingCapacity)
                return $"На складах товары! Продай товар или купи транспорт побольше.";

            var sellPrice = warehouse.PurchasePrice * 0.5m;
            var warehouseName = $"{warehouse.Emoji} {warehouse.Name}";

            player.Money += sellPrice;
            player.Warehouses.Remove(warehouse);

            LogActivity($"{player.Name} продал {warehouseName} за {sellPrice:C}");
            OnStateChanged?.Invoke();
            return $"Продал {warehouseName} за {sellPrice:C}";
        }

        public void ProcessWarehouseMaintenance(Player player)
        {
            if (!player.Warehouses.Any()) return;

            foreach (var warehouse in player.Warehouses.ToList())
            {
                var daysSinceMaintenance = (CurrentTime - warehouse.LastMaintenancePaid).Days;
                if (daysSinceMaintenance >= GameConstants.WarehouseMaintenanceDays)
                {
                    if (player.Money >= warehouse.MonthlyMaintenance)
                    {
                        player.Money -= warehouse.MonthlyMaintenance;
                        warehouse.LastMaintenancePaid = CurrentTime;
                        LogActivity($"{player.Name} заплатил за содержание {warehouse.Name} {warehouse.MonthlyMaintenance:C}");
                    }
                    else
                    {
                        // Can't pay - lose warehouse but keep goods if capacity allows
                        var remainingCapacity = player.Vehicles.Sum(v => v.CargoCapacity) + 
                            player.Warehouses.Where(w => w.WarehouseId != warehouse.WarehouseId).Sum(w => w.Capacity);
                        if (player.CurrentCargoUsed <= remainingCapacity)
                        {
                            LogActivity($"{player.Name} не смог оплатить {warehouse.Name} и потерял его!");
                            player.Warehouses.Remove(warehouse);
                        }
                        else
                        {
                            LogActivity($"{player.Name} задолжал за содержание {warehouse.Name}!");
                        }
                    }
                }
            }
        }

        public void ProcessFactoryMaintenance(Player player)
        {
            if (player.Factories == null || !player.Factories.Any()) return;

            foreach (var factory in player.Factories)
            {
                var daysSinceMaintenance = (CurrentTime - factory.LastMaintenancePaid).Days;
                if (daysSinceMaintenance >= GameConstants.WarehouseMaintenanceDays) // Reuse same cycle
                {
                    if (player.Money >= factory.MonthlyMaintenance)
                    {
                        player.Money -= factory.MonthlyMaintenance;
                        factory.LastMaintenancePaid = CurrentTime;
                        factory.IsOperational = true;
                        LogActivity($"{player.Name} заплатил за содержание {factory.Name} {factory.MonthlyMaintenance:C}");
                    }
                    else
                    {
                        // Can't pay - factory stops working but you keep it (expensive to build)
                        if (factory.IsOperational)
                        {
                            factory.IsOperational = false;
                            LogActivity($"{player.Name} не смог оплатить содержание {factory.Name}. Производство остановлено!");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Harvest-cycle agriculture (economy rebalance): a plantation ripens for N game days
        /// and yields one harvest, instead of dripping resources every tick.
        /// Diseases can strike mid-cycle; they are auto-cured at harvest if the owner
        /// has enough Chemicals in stock, otherwise the yield is slashed.
        /// </summary>
        private void ProcessAgriculturalGrowth(Player player)
        {
            if (player.Factories == null || !player.Factories.Any()) return;

            foreach (var factory in player.Factories)
            {
                if (!factory.IsOperational) continue;

                (string? rawItemName, int cycleDays, int cycleYield) = factory.Type switch
                {
                    FactoryType.SugarCanePlantation => ("SugarCane", GameConstants.SugarCaneCycleDays, GameConstants.SugarCaneCycleYield),
                    FactoryType.CoffeePlantation => ("CoffeeBeans", GameConstants.CoffeeCycleDays, GameConstants.CoffeeCycleYield),
                    FactoryType.WheatFarm => ("Wheat", GameConstants.WheatCycleDays, GameConstants.WheatCycleYield),
                    _ => (null, 0, 0)
                };

                if (rawItemName == null) continue;

                // Start the first cycle lazily (also covers factories from old saves)
                factory.CurrentCycleStart ??= CurrentTime;

                // Disease can strike mid-cycle. Per-tick chance is derived from the
                // per-cycle chance spread across all ticks of the cycle (96 ticks per game day).
                if (!factory.IsDiseased)
                {
                    double perTickDiseaseChance = GameConstants.CropDiseaseChancePerCycle / (cycleDays * 96.0);
                    if (_random.NextDouble() < perTickDiseaseChance)
                    {
                        factory.IsDiseased = true;
                        LogActivity($"🦠 [Агро] Болезнь поразила {factory.Name}! Запасись химикатами ({GameConstants.CropCureChemicalsCost} шт.) до сбора урожая, иначе потеряешь часть урожая.");
                    }
                }

                // Not ripe yet?
                if ((CurrentTime - factory.CurrentCycleStart.Value).TotalDays < cycleDays) continue;

                int grownQty = cycleYield * Math.Max(1, factory.ProductionLevel);

                // Handle disease at harvest: auto-cure with Chemicals if available
                if (factory.IsDiseased)
                {
                    var chemicals = player.Inventory.FirstOrDefault(i => i.ItemName == GameConstants.Chemicals);
                    if (chemicals != null && chemicals.Quantity >= GameConstants.CropCureChemicalsCost)
                    {
                        chemicals.Quantity -= GameConstants.CropCureChemicalsCost;
                        if (chemicals.Quantity <= 0) player.Inventory.Remove(chemicals);
                        LogActivity($"🧪 [Агро] {player.Name} вылечил урожай на {factory.Name} химикатами — урожай спасён!");
                    }
                    else
                    {
                        var survivalFactor = 0.3 + _random.NextDouble() * 0.4; // 30-70% of yield survives
                        grownQty = (int)(grownQty * survivalFactor);
                        LogActivity($"🦠 [Агро] Болезнь уничтожила часть урожая на {factory.Name} — собрано лишь {grownQty} ед.");
                    }
                    factory.IsDiseased = false;
                }

                int space = player.AvailableCargoSpace;
                int actualGrown = Math.Min(grownQty, Math.Max(0, space));
                if (actualGrown > 0)
                {
                    var marketItem = ExchangeItems.FirstOrDefault(i => i.Name == rawItemName);
                    var price = marketItem?.CurrentPrice ?? 50m;
                    player.Inventory.AddOrUpdateItem(rawItemName, price, actualGrown);
                    LogActivity($"🌾 [Агро] Урожай собран на {factory.Name}: +{actualGrown} ед. {rawItemName}");
                    if (actualGrown < grownQty)
                    {
                        LogActivity($"⚠️ [Агро] {grownQty - actualGrown} ед. {rawItemName} сгнило — не хватило места на складах!");
                    }
                }
                else if (grownQty > 0)
                {
                    LogActivity($"⚠️ [Агро] Урожай на {factory.Name} сгнил — некуда складывать! Освободи место на складах.");
                }

                // Start the next cycle
                factory.CurrentCycleStart = CurrentTime;
            }
        }

        // Legacy method for backward compatibility
        public string BuyCar(Player player)
        {
            return BuyVehicle(player, VehicleType.BasicCar);
        }

        /// <summary>
        /// Buy a trading license to expand warehouse capacity.
        /// </summary>
        public string BuyTradingLicense(Player player, int level)
        {
            if (player == null) return "Игрок не найден!";
            if (level < 1 || level > 3) return "Уровень лицензии должен быть 1-3!";
            if (player.TradingLicenseLevel >= level) return $"У тебя уже есть лицензия уровня {player.TradingLicenseLevel}!";
            if (level > player.TradingLicenseLevel + 1) return $"Сначала купи лицензию уровня {player.TradingLicenseLevel + 1}!";

            var cost = level switch
            {
                1 => GameConstants.TradingLicenseLevel1Cost,
                2 => GameConstants.TradingLicenseLevel2Cost,
                3 => GameConstants.TradingLicenseLevel3Cost,
                _ => 0m
            };

            if (player.Money < cost) return $"Не хватает денег! Нужно {cost:C}";

            player.Money -= cost;
            player.TradingLicenseLevel = level;

            LogActivity($"{player.Name} купил торговую лицензию уровня {level} за {cost:C}");
            OnStateChanged?.Invoke();
            return $"🎫 Торговая лицензия уровня {level}! Теперь можно иметь {player.MaxWarehouses} складов.";
        }

        public void ProcessOverloadPenalty(Player player)
        {
            if (player.AvailableCargoSpace >= 0) return; // Not overloaded
            if (player.IsAI) return; // Skip AI for now
            
            // Player is overloaded!
            var excessCargo = -player.AvailableCargoSpace;
            
            // Random chance to lose cargo
            if (_random.NextDouble() >= GameConstants.OverloadLossChance) return;
            
            // Calculate how much to lose (10% of excess, min 1)
            var cargoToLose = Math.Max(1, (int)(excessCargo * GameConstants.OverloadLossPercent));
            
            // Pick a random item to lose from
            var itemsWithQuantity = player.Inventory.Where(i => i.Quantity > 0).ToList();
            if (!itemsWithQuantity.Any()) return;
            
            var lostItem = itemsWithQuantity[_random.Next(itemsWithQuantity.Count)];
            var actualLoss = Math.Min(cargoToLose, lostItem.Quantity);
            
            lostItem.Quantity -= actualLoss;
            if (lostItem.Quantity <= 0)
                player.Inventory.Remove(lostItem);
            
            // Create event notification
            var lossEvent = new GameEvent
            {
                Title = "🚗💨 Перегруз!",
                Description = $"Машина перегружена! По дороге выпало {actualLoss} {lostItem.ItemName}. Купи транспорт побольше или склад!",
                Choices = new List<EventChoice>
                {
                    new() { ChoiceId = 1, Text = "Понятно...", Cost = 0, OutcomeDescription = "Нужно следить за вместимостью!" }
                },
                ExpiresAt = CurrentTime.AddMinutes(30),
                TargetPlayerId = player.Id
            };
            player.PendingEvents.Add(lossEvent);
            
            LogActivity($"⚠️ {player.Name} потерял {actualLoss} {lostItem.ItemName} из-за перегруза!");
        }

        private void ProcessAutoProduction(Player player)
        {
            if (player.IsSabotaged) return; // Production stops when sabotaged
            if (player.AutoProductionRecipes == null || !player.AutoProductionRecipes.Any()) return;
            if (!player.Warehouses.Any()) return;

            foreach (var recipeId in player.AutoProductionRecipes)
            {
                var recipe = ProductionRecipes.AllRecipes.FirstOrDefault(r => r.RecipeId == recipeId);
                if (recipe == null) continue;

                // --- Recipes take real time now (economy rebalance) ---
                // ProductionTime is measured in ticks (1 tick = 15 game minutes).
                // Progress accumulates every tick; the batch executes only when the cycle completes.
                int progress = player.AutoProductionProgress.GetValueOrDefault(recipeId) + 1;
                if (progress < recipe.ProductionTime)
                {
                    player.AutoProductionProgress[recipeId] = progress;
                    continue;
                }
                // Hold the completed cycle until a batch actually runs (e.g. waiting for inputs),
                // so scarce resources don't silently waste a whole cycle.
                player.AutoProductionProgress[recipeId] = recipe.ProductionTime;

                // Find upgrade level
                int level = 1;
                if (player.AutoProductionLevels != null && player.AutoProductionLevels.TryGetValue(recipeId, out int val))
                {
                    level = val;
                }
                
                // Throughput multiplier based on level: Lvl 1=1x, Lvl 2=2x, Lvl 3=3x, Lvl 4=5x
                int speedMultiplier = level switch
                {
                    2 => 2,
                    3 => 3,
                    4 => 5,
                    _ => 1
                };

                // Determine maximum batches we can run (up to speedMultiplier) based on cash, resources, cargo, and max stock limits
                int batches = speedMultiplier;

                // 1. Cash constraint
                if (recipe.ProductionCost > 0)
                {
                    int maxCashBatches = (int)Math.Clamp(player.Money / recipe.ProductionCost, 0m, (decimal)int.MaxValue);
                    if (maxCashBatches < batches) batches = maxCashBatches;
                }

                // 2. Resource constraints
                foreach (var input in recipe.Inputs)
                {
                    var invItem = player.Inventory.FirstOrDefault(i => i.ItemName == input.Key);
                    int minReserve = 0;
                    if (player.AutoProductionMinReserves != null && player.AutoProductionMinReserves.TryGetValue(recipeId, out int valReserve))
                    {
                        minReserve = valReserve;
                    }

                    int availableQty = (invItem != null) ? (invItem.Quantity - minReserve) : 0;
                    if (availableQty < 0) availableQty = 0;

                    int maxResourceBatches = availableQty / input.Value;
                    if (maxResourceBatches < batches) batches = maxResourceBatches;
                }

                // 3. Storage space constraint
                var outputTotal = recipe.Outputs.Sum(o => o.Value);
                var inputTotal = recipe.Inputs.Sum(i => i.Value);
                var netChangePerBatch = outputTotal - inputTotal;
                if (netChangePerBatch > 0)
                {
                    int maxSpaceBatches = (int)Math.Clamp(player.AvailableCargoSpace / netChangePerBatch, 0m, (decimal)int.MaxValue);
                    if (maxSpaceBatches < batches) batches = maxSpaceBatches;
                }

                // 4. Max stock constraint
                foreach (var output in recipe.Outputs)
                {
                    var invItem = player.Inventory.FirstOrDefault(i => i.ItemName == output.Key);
                    int maxStock = int.MaxValue;
                    if (player.AutoProductionMaxStock != null && player.AutoProductionMaxStock.TryGetValue(recipeId, out int valMax))
                    {
                        maxStock = valMax;
                    }

                    int currentQty = invItem?.Quantity ?? 0;
                    int spaceToMax = maxStock - currentQty;
                    if (spaceToMax < 0) spaceToMax = 0;

                    int maxStockBatches = spaceToMax / output.Value;
                    if (maxStockBatches < batches) batches = maxStockBatches;
                }

                if (batches <= 0) continue;

                // A batch is actually running — restart the production cycle
                player.AutoProductionProgress[recipeId] = 0;

                // Execute production for 'batches' iterations
                player.Money -= recipe.ProductionCost * batches;

                // Move inputs
                foreach (var input in recipe.Inputs)
                {
                    player.Inventory.RemoveQuantity(input.Key, input.Value * batches);
                }

                // Add outputs
                var efficiencyMultiplier = 1.0m;
                var matchingFactory = player.Factories.FirstOrDefault(f => 
                    AvailableFactories.Any(af => af.Type == f.Type && af.TargetRecipe == recipe.Name));
                
                if (matchingFactory != null && matchingFactory.IsOperational)
                {
                    efficiencyMultiplier = matchingFactory.EfficiencyMultiplier;
                }

                foreach (var output in recipe.Outputs)
                {
                    var marketItem = ExchangeItems.FirstOrDefault(i => i.Name == output.Key);
                    var price = marketItem?.CurrentPrice ?? 100m;
                    int quantityWithEfficiency = (int)(output.Value * batches * efficiencyMultiplier);
                    player.Inventory.AddOrUpdateItem(output.Key, price, quantityWithEfficiency);
                }

                LogActivity($"[AUTO] {player.Name} произвёл {recipe.Name} (x{batches})");
            }
        }

        public string UpgradeAutoProductionSpeed(Player player, Guid recipeId)
        {
            if (player == null) return "Игрок не найден!";

            var recipe = ProductionRecipes.AllRecipes.FirstOrDefault(r => r.RecipeId == recipeId);
            if (recipe == null) return "Рецепт не найден!";

            if (player.AutoProductionLevels == null)
            {
                player.AutoProductionLevels = new Dictionary<Guid, int>();
            }

            int currentLevel = 1;
            if (player.AutoProductionLevels.TryGetValue(recipeId, out int val))
            {
                currentLevel = val;
            }

            if (currentLevel >= 4)
            {
                return "Максимальный уровень автоматизации уже достигнут!";
            }

            int nextLevel = currentLevel + 1;
            decimal cost = nextLevel switch
            {
                2 => 50000m,
                3 => 200000m,
                4 => 1000000m,
                _ => 0m
            };

            if (player.Money < cost)
            {
                return $"Не хватает денег! Нужно {cost:C}, у тебя {player.Money:C}";
            }

            player.Money -= cost;
            player.AutoProductionLevels[recipeId] = nextLevel;

            LogActivity($"{player.Name} улучшил автоматизацию производства {recipe.Name} до уровня {nextLevel} за {cost:C}");
            OnStateChanged?.Invoke();
            return $"Успешно улучшено! Теперь уровень {nextLevel}.";
        }

        #endregion

        #region News System

        private static readonly string[] GoodNewsTemplates = {
            "High Demand for {0}!|Analysts report a surge in demand for {0} globally.",
            "{0} Shortage Looms|Supply chain issues may lead to {0} shortage.",
            "Investors Flock to {0}|Major funds are increasing {0} holdings.",
            "{0} Export Boom|International buyers are snapping up {0}."
        };

        private static readonly string[] BadNewsTemplates = {
            "{0} Market Crash!|Oversupply of {0} floods the market.",
            "{0} Demand Slump|Consumer interest in {0} is waning.",
            "New {0} Alternatives|Cheaper substitutes threaten {0} market share.",
            "{0} Quality Concerns|Reports of quality issues hurt {0} prices."
        };

        private void GenerateNews()
        {
            // Small chance per tick for news
            if (_random.NextDouble() >= GameConstants.NewsGenerationChance) return;

            var item = _exchange.Items[_random.Next(_exchange.Items.Count)];
            var isGoodNews = _random.NextDouble() > 0.5;
            var impact = (decimal)(_random.NextDouble() * 0.4 + 0.1); // 10% to 50% impact

            var templates = isGoodNews ? GoodNewsTemplates : BadNewsTemplates;
            var template = templates[_random.Next(templates.Length)];
            var parts = template.Split('|');
            
            var title = string.Format(parts[0], item.Name);
            var content = string.Format(parts[1], item.Name);

            // 30% chance to be a rumor with delayed effect
            var isRumor = _random.NextDouble() < 0.3;

            var news = new News
            {
                Title = isRumor ? $"[RUMOR] {title}" : title,
                Content = content,
                Timestamp = CurrentTime,
                MarketImpact = isGoodNews ? impact : -impact,
                AffectedItemName = item.Name,
                Type = isRumor ? NewsType.Rumor : NewsType.Breaking,
                EffectTime = isRumor ? CurrentTime.AddMinutes(_random.Next(30, 120)) : null,
                IsApplied = false,
                ConfirmationChance = isRumor ? 0.7 : 1.0 // 70% chance rumor comes true
            };

            // Apply effect immediately if not a rumor
            if (news.Type == NewsType.Breaking)
            {
                ApplyNewsEffect(news, item);
            }

            lock (_logLock)
            {
                _news.Add(news);
                if (_news.Count > GameConstants.MaxNewsEntries) _news.RemoveAt(0);
            }
        }

        private void ProcessPendingNewsEffects()
        {
            List<News> rumors;
            lock (_logLock)
            {
                rumors = _news.Where(n => n.Type == NewsType.Rumor && !n.IsApplied && n.EffectTime <= CurrentTime).ToList();
            }

            foreach (var news in rumors)
            {
                var item = _exchange.Items.FirstOrDefault(i => i.Name == news.AffectedItemName);
                if (item == null) continue;

                // Check if rumor comes true
                if (_random.NextDouble() < news.ConfirmationChance)
                {
                    ApplyNewsEffect(news, item);
                    
                    // Create confirmation news
                    var confirmation = new News
                    {
                        Title = news.Title.Replace("[RUMOR]", "[CONFIRMED]"),
                        Content = $"Earlier rumors about {item.Name} have been confirmed!",
                        Timestamp = CurrentTime,
                        MarketImpact = news.MarketImpact,
                        AffectedItemName = item.Name,
                        Type = NewsType.Confirmed,
                        IsApplied = true
                    };
                    lock (_logLock)
                    {
                        _news.Add(confirmation);
                    }
                }
                else
                {
                    // Rumor was false
                    var debunk = new News
                    {
                        Title = $"{item.Name} Rumor Debunked",
                        Content = $"Earlier rumors about {item.Name} were false. Markets stabilize.",
                        Timestamp = CurrentTime,
                        MarketImpact = 0,
                        AffectedItemName = item.Name,
                        Type = NewsType.Breaking,
                        IsApplied = true
                    };
                    lock (_logLock)
                    {
                        _news.Add(debunk);
                    }
                }
                
                news.IsApplied = true;
            }
        }

        private void ApplyNewsEffect(News news, MarketItem item)
        {
            item.CurrentPrice *= (1 + news.MarketImpact);
            news.IsApplied = true;
        }

        // Disaster catalogue: richer variety so the economy doesn't rely on Oil/Wheat shocks only.
        private static readonly (string ItemName, double LossPercent, string DisasterName, string Template)[] DisasterPool =
        {
            ("Oil",          0.50, "Землетрясение",     "Мощное землетрясение разрушило нефтяные терминалы! 50% запасов {0} потеряно."),
            ("Wheat",        0.70, "Наводнение",        "Сильное наводнение затопило зернохранилища! 70% запасов {0} уничтожено."),
            ("Steel",        0.40, "Забастовка",        "Металлурги объявили общенациональную забастовку — 40% производства {0} остановлено."),
            ("Copper",       0.45, "Обвал шахты",       "Обрушение в крупной шахте: 45% запасов {0} недоступны."),
            ("Gold",         0.30, "Вооружённый налёт", "Налёт на хранилище: 30% запасов {0} пропало."),
            ("SugarCane",    0.55, "Засуха",            "Сильная засуха выжгла плантации — 55% {0} потеряно."),
            ("CoffeeBeans",  0.50, "Заморозки",         "Неожиданные заморозки уничтожили 50% урожая {0}."),
        };

        private void GenerateDisaster()
        {
            // Small chance for a disaster (2% per tick)
            if (_random.NextDouble() >= 0.02) return;

            var disaster = DisasterPool[_random.Next(DisasterPool.Length)];
            string itemName = disaster.ItemName;
            double lossPercent = disaster.LossPercent;
            string disasterName = disaster.DisasterName;
            string description = string.Format(disaster.Template, itemName);

            // 1. Affect Market Stock
            var marketItem = _exchange.Items.FirstOrDefault(i => i.Name == itemName);
            if (marketItem != null)
            {
                marketItem.AvailableQuantity = (int)(marketItem.AvailableQuantity * (1 - lossPercent));
            }

            // 2. Affect All Players
            foreach (var player in _playerService.GetAllPlayers())
            {
                var invItem = player.Inventory.FirstOrDefault(i => i.ItemName == itemName);
                if (invItem != null && invItem.Quantity > 0)
                {
                    int lossQuantity = (int)(invItem.Quantity * lossPercent);
                    if (lossQuantity > 0)
                    {
                        var lossValue = lossQuantity * marketItem?.CurrentPrice ?? 0;
                        player.Inventory.RemoveQuantity(itemName, lossQuantity);
                        LogActivity($"💥 {player.Name} потерял {lossQuantity} {itemName} из-за {disasterName.ToLower()}!");

                        // Process Insurance Claim
                        if (player.HasInsurance)
                        {
                            var (isCovered, message) = _insuranceService.ProcessClaim(player, lossValue);
                            if (isCovered)
                            {
                                LogActivity(message);
                                // Optional: add a news entry for the claim if it's the main player
                            }
                        }
                    }
                }
            }

            // 3. Log as News
            var news = new News
            {
                Title = $"🔥 КАТАСТРОФА: {disasterName}!",
                Content = description,
                Timestamp = CurrentTime,
                MarketImpact = (decimal)(lossPercent * 0.5), // Disasters also spike prices due to scarcity
                AffectedItemName = itemName,
                Type = NewsType.Breaking,
                IsApplied = true
            };

            // Spike the price immediately due to scarcity
            if (marketItem != null)
            {
                marketItem.CurrentPrice *= (1 + news.MarketImpact);
            }

            lock (_logLock)
            {
                _news.Add(news);
                if (_news.Count > GameConstants.MaxNewsEntries) _news.RemoveAt(0);
            }

            LogActivity($"🚨 ПРОИЗОШЛО {disasterName.ToUpper()}! Запасы {itemName} резко сократились.");
            OnStateChanged?.Invoke();
        }

        #endregion

        #region Trading

        public List<MarketItem> ExchangeItems => _exchange.Items.ToList();
        public News? LatestNews 
        {
            get
            {
                lock (_logLock)
                {
                    return _news.LastOrDefault();
                }
            }
        }
        public List<News> AllNews
        {
            get
            {
                lock (_logLock)
                {
                    return _news.ToList();
                }
            }
        }
        public Bank Bank => _bank;

        public string BuyItem(Player player, MarketItem item, int quantity)
        {
            if (player == null) return "No player found!";
            
            if (player.IsSabotaged)
                return "Ваши операции заблокированы из-за саботажа! Дождитесь окончания действия эффекта.";
            
            var totalCost = item.CurrentPrice * quantity;

            if (player.Money < totalCost)
                return $"Not enough money! Need {totalCost:C}, you have {player.Money:C}";

            if (item.AvailableQuantity < quantity)
                return $"Not enough {item.Name} available! Only {item.AvailableQuantity} in stock.";

            // Phase 9: Logistics Constraints
            var totalFleetCapacity = player.Vehicles.Sum(v => v.CargoCapacity);
            if (totalFleetCapacity > 0 && quantity > totalFleetCapacity)
                return $"Ваш автопарк может перевозить не более {totalFleetCapacity} ед. за раз. Совершите несколько поездок или расширьте автопарк.";

            if (quantity > player.AvailableCargoSpace)
                return $"Недостаточно места на складах! Свободно: {player.AvailableCargoSpace} ед.";

            // Execute purchase
            player.Money -= totalCost;
            item.AvailableQuantity -= quantity;
            item.BuyVolume += (decimal)quantity; // Track volume for market price impact

            // Use extension method for inventory management
            player.Inventory.AddOrUpdateItem(item.Name, item.CurrentPrice, quantity);

            // Award reputation for successful trade
            AwardReputation(player, GameConstants.ReputationGainPerTrade);

            LogActivity($"{player.Name} bought {quantity} {item.Name} for {totalCost:C}");

            OnStateChanged?.Invoke();
            return $"Successfully bought {quantity} {item.Name} for {totalCost:C}";
        }

        public string SellItem(Player player, MarketItem item, int quantity)
        {
            if (player == null) return "No player found!";

            if (player.IsSabotaged)
                return "Ваши операции заблокированы из-за саботажа! Продажа невозможна.";

            var inventoryItem = player.Inventory.FirstOrDefault(i => i.ItemName == item.Name);
            if (inventoryItem == null)
                return $"You don't have any {item.Name} to sell!";

            // FIX: Don't silently adjust quantity - return error instead
            if (quantity > inventoryItem.Quantity)
                return $"You only have {inventoryItem.Quantity} {item.Name}! Cannot sell {quantity}.";

            var totalRevenue = item.CurrentPrice * quantity;

            // Execute sale
            player.Money += totalRevenue;
            item.AvailableQuantity += quantity;
            item.SellVolume += (decimal)quantity; // Track volume for market price impact
            
            // Use extension method
            player.Inventory.RemoveQuantity(item.Name, quantity);

            // Award reputation
            AwardReputation(player, GameConstants.ReputationGainPerTrade);

            LogActivity($"{player.Name} sold {quantity} {item.Name} for {totalRevenue:C}");

            OnStateChanged?.Invoke();
            return $"Successfully sold {quantity} {item.Name} for {totalRevenue:C}";
        }

        #endregion

        #region Trade Listings (Player-to-Player)

        public List<TradeListing> TradeListings => _exchange.Listings.ToList();

        public string CreateListing(Player seller, string itemName, int quantity, decimal pricePerUnit, bool autoRepeat = false)
        {
            if (seller == null) return "Seller not found!";
            
            if (seller.IsSabotaged)
                return "Контракты заблокированы из-за саботажа!";
            
            var inventoryItem = seller.Inventory.FirstOrDefault(i => i.ItemName == itemName);
            if (inventoryItem == null || inventoryItem.Quantity < quantity)
                return $"Not enough {itemName} in inventory!";

            // Remove from inventory using extension
            if (!seller.Inventory.RemoveQuantity(itemName, quantity))
                return $"Failed to reserve {itemName} for listing!";

            var listing = new TradeListing
            {
                SellerId = seller.Id,
                SellerName = seller.Name,
                ItemName = itemName,
                Quantity = quantity,
                PricePerUnit = pricePerUnit,
                AutoRepeat = autoRepeat
            };
            
            _exchange.Listings.Add(listing);
            OnStateChanged?.Invoke();
            return $"Listed {quantity} {itemName} for {pricePerUnit:C} each";
        }

        public string BuyListing(Player buyer, TradeListing listing)
        {
            if (buyer == null) return "Buyer not found!";
            if (!_exchange.Listings.Contains(listing)) return "Listing no longer exists!";
            
            if (buyer.Id == listing.SellerId) return "Cannot buy your own listing!";

            if (buyer.Money < listing.TotalPrice)
                return $"Not enough money! Need {listing.TotalPrice:C}";

            var seller = _playerService.GetPlayer(listing.SellerId);
            if (seller == null) 
            {
                _exchange.Listings.Remove(listing);
                return "Seller account not found. Listing removed.";
            }

            // Transaction
            buyer.Money -= listing.TotalPrice;
            
            // Seller gets amount minus commission
            var sellerRevenue = listing.TotalPrice * (1 - GameConstants.SellerFeeRate);
            seller.Money += sellerRevenue;

            // Buyer gets item using extension
            buyer.Inventory.AddOrUpdateItem(listing.ItemName, listing.PricePerUnit, listing.Quantity);

            // Award reputation to both parties
            AwardReputation(buyer, GameConstants.ReputationGainPerTrade);
            AwardReputation(seller, GameConstants.ReputationGainPerTrade);

            LogActivity($"{buyer.Name} bought listing: {listing.Quantity} {listing.ItemName} from {listing.SellerName} for {listing.TotalPrice:C}");

            _exchange.Listings.Remove(listing);

            // Handle Auto-Repeat
            if (listing.AutoRepeat)
            {
                var repeatResult = CreateListing(seller, listing.ItemName, listing.Quantity, listing.PricePerUnit, true);
                if (repeatResult.StartsWith("Listed"))
                {
                    LogActivity($"[AUTO-MARKET] {seller.Name} автоматически перевыставил {listing.Quantity} {listing.ItemName}");
                }
                else
                {
                    LogActivity($"[AUTO-MARKET] {seller.Name} не смог перевыставить {listing.ItemName}: {repeatResult}");
                }
            }

            OnStateChanged?.Invoke();
            return $"Bought {listing.Quantity} {listing.ItemName} for {listing.TotalPrice:C}. Seller received {sellerRevenue:C}";
        }

        public string CancelListing(Player seller, TradeListing listing)
        {
            if (seller == null) return "Seller not found!";
            if (!_exchange.Listings.Contains(listing)) return "Listing no longer exists!";
            if (listing.SellerId != seller.Id) return "Not your listing!";

            var penalty = listing.TotalPrice * GameConstants.CancellationPenaltyRate;
            if (seller.Money < penalty)
                return $"Cannot cancel! You need {penalty:C} for the cancellation fee.";

            // Pay penalty
            seller.Money -= penalty;

            // Return item using extension
            seller.Inventory.AddOrUpdateItem(listing.ItemName, listing.PricePerUnit, listing.Quantity);

            // Penalize reputation for cancellation
            PenalizeReputation(seller, GameConstants.ReputationLossPerCancellation);

            _exchange.Listings.Remove(listing);
            OnStateChanged?.Invoke();
            return $"Listing cancelled. Paid {penalty:C} fee.";
        }

        #endregion

        #region Reputation System

        private void AwardReputation(Player player, int amount)
        {
            player.Reputation = Math.Min(100, player.Reputation + amount);
        }

        private void PenalizeReputation(Player player, int amount)
        {
            player.Reputation = Math.Max(0, player.Reputation - amount);
        }

        #endregion

        #region Interactive Events

        private static readonly (string Title, string Description, List<EventChoice> Choices, decimal MinMoney)[] EventTemplates = {
            (
                "Storm Warning!",
                "A major storm is approaching. It could damage stored goods.",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Buy insurance ($500)", Cost = 500, OutcomeDescription = "Your goods are protected.", MoneyChange = -500 },
                    new() { ChoiceId = 2, Text = "Risk it", Cost = 0, OutcomeDescription = "Let's hope for the best...", MoneyChange = 0 }
                },
                0
            ),
            (
                "Investment Opportunity",
                "A trader offers you an exclusive deal on bulk goods. High risk, high reward!",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Invest $1000", Cost = 1000, OutcomeDescription = "You take the gamble.", MoneyChange = -1000 },
                    new() { ChoiceId = 2, Text = "Pass", Cost = 0, OutcomeDescription = "You play it safe.", MoneyChange = 0 }
                },
                1000
            ),
            (
                "Charity Request",
                "A local charity asks for a donation. It would boost your reputation.",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Donate $200", Cost = 200, OutcomeDescription = "Your generosity is appreciated!", MoneyChange = -200, ReputationChange = 10 },
                    new() { ChoiceId = 2, Text = "Decline politely", Cost = 0, OutcomeDescription = "Maybe next time.", MoneyChange = 0, ReputationChange = -2 }
                },
                200
            ),
            (
                "Lucky Find!",
                "You found a valuable item on the street!",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Keep it", Cost = 0, OutcomeDescription = "Finders keepers!", MoneyChange = 300, ReputationChange = -5 },
                    new() { ChoiceId = 2, Text = "Turn it in", Cost = 0, OutcomeDescription = "The owner is grateful.", MoneyChange = 50, ReputationChange = 5 }
                },
                0
            ),
            // Life-sim events
            (
                "Тяжёлая ночь",
                "У тебя нет жилья, пришлось ночевать в машине. Утром чувствуешь себя отвратительно...",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Пойти в больницу ($800)", Cost = 800, OutcomeDescription = "Врач выписал антибиотики. Через пару дней будешь как новенький!", MoneyChange = -800 },
                    new() { ChoiceId = 2, Text = "Перетерпеть", Cost = 0, OutcomeDescription = "Состояние ухудшилось... Пришлось ехать в скорую. Счёт вдвое больше.", MoneyChange = -1500 }
                },
                0
            ),
            (
                "Встреча в баре",
                "Познакомился с мужиком в баре. Выпили по паре кружек. Он предлагает купить 100 единиц зерна по смешной цене!",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Купить! ($200)", Cost = 200, OutcomeDescription = "Сделка! 100 Wheat теперь твои.", MoneyChange = -200, ItemReward = "Wheat", ItemQuantity = 100 },
                    new() { ChoiceId = 2, Text = "Слишком хорошо, отказаться", Cost = 0, OutcomeDescription = "Осторожность не повредит. Он ушёл расстроенный.", MoneyChange = 0, ReputationChange = -1 }
                },
                200
            ),
            (
                "День Рождения! 🎂",
                "Сегодня твой ДР! Живёшь в маленькой комнате, но пришли 2 друга с подарками.",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Устроить вечеринку ($150)", Cost = 150, OutcomeDescription = "Отличный вечер! Друзья скинулись на подарок.", MoneyChange = 100, ReputationChange = 5 },
                    new() { ChoiceId = 2, Text = "Посидеть скромно", Cost = 0, OutcomeDescription = "Тихо посидели с чаем. Подарили немного денег.", MoneyChange = 50, ReputationChange = 2 }
                },
                150
            ),
            (
                "Подозрительный тип",
                "Какой-то тип предлагает 'инвестицию'. Гарантирует 200% через неделю!",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Вложить $500", Cost = 500, OutcomeDescription = "Скорее всего это развод... но вдруг повезёт?", MoneyChange = -500 },
                    new() { ChoiceId = 2, Text = "Отказаться", Cost = 0, OutcomeDescription = "Здравый смысл победил. Он исчез в толпе.", MoneyChange = 0, ReputationChange = 1 }
                },
                500
            ),
            (
                "Случайная работа",
                "Знакомый предлагает подработку на складе. Тяжело, но честно.",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Поработать (4 часа)", Cost = 0, OutcomeDescription = "Заработал честные деньги и уважение.", MoneyChange = 300, ReputationChange = 3 },
                    new() { ChoiceId = 2, Text = "Отказаться", Cost = 0, OutcomeDescription = "Сегодня не твой день.", MoneyChange = 0 }
                },
                0
            ),
            // Premium Crisis Events
            (
                "event.robbery.title",
                "event.robbery.desc",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "event.robbery.choice1", Cost = 15000, OutcomeDescription = "event.robbery.choice1.desc", MoneyChange = -15000 },
                    new() { ChoiceId = 2, Text = "event.robbery.choice2", Cost = 0, OutcomeDescription = "event.robbery.choice2.desc", MoneyChange = -35000, ReputationChange = -15 }
                },
                15000
            ),
            (
                "event.audit.title",
                "event.audit.desc",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "event.audit.choice1", Cost = 25000, OutcomeDescription = "event.audit.choice1.desc", MoneyChange = -25000, ReputationChange = 5 },
                    new() { ChoiceId = 2, Text = "event.audit.choice2", Cost = 0, OutcomeDescription = "event.audit.choice2.desc", MoneyChange = -50000, ReputationChange = -5 }
                },
                50000
            ),
            (
                "event.fire.title",
                "event.fire.desc",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "event.fire.choice1", Cost = 8000, OutcomeDescription = "event.fire.choice1.desc", MoneyChange = -8000, ReputationChange = 2 },
                    new() { ChoiceId = 2, Text = "event.fire.choice2", Cost = 0, OutcomeDescription = "event.fire.choice2.desc", MoneyChange = -30000, ReputationChange = -5 }
                },
                15000
            ),
            (
                "event.mafia.title",
                "event.mafia.desc",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "event.mafia.choice1", Cost = 12000, OutcomeDescription = "event.mafia.choice1.desc", MoneyChange = -12000, ReputationChange = -8 },
                    new() { ChoiceId = 2, Text = "event.mafia.choice2", Cost = 20000, OutcomeDescription = "event.mafia.choice2.desc", MoneyChange = -20000, ReputationChange = 15 },
                    new() { ChoiceId = 3, Text = "event.mafia.choice3", Cost = 0, OutcomeDescription = "event.mafia.choice3.desc", MoneyChange = -40000, ReputationChange = -10 }
                },
                20000
            ),
            (
                "event.flood.title",
                "event.flood.desc",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "event.flood.choice1", Cost = 6000, OutcomeDescription = "event.flood.choice1.desc", MoneyChange = -6000, ReputationChange = 2 },
                    new() { ChoiceId = 2, Text = "event.flood.choice2", Cost = 0, OutcomeDescription = "event.flood.choice2.desc", MoneyChange = -25000, ReputationChange = -8 }
                },
                15000
            ),
            (
                "event.crisis.title",
                "event.crisis.desc",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "event.crisis.choice1", Cost = 12000, OutcomeDescription = "event.crisis.choice1.desc", MoneyChange = -12000, ReputationChange = -3 },
                    new() { ChoiceId = 2, Text = "event.crisis.choice2", Cost = 0, OutcomeDescription = "event.crisis.choice2.desc", MoneyChange = -30000, ReputationChange = -10 }
                },
                40000
            ),
            (
                "event.cyber.title",
                "event.cyber.desc",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "event.cyber.choice1", Cost = 15000, OutcomeDescription = "event.cyber.choice1.desc", MoneyChange = -15000, ReputationChange = -5 },
                    new() { ChoiceId = 2, Text = "event.cyber.choice2", Cost = 20000, OutcomeDescription = "event.cyber.choice2.desc", MoneyChange = -20000, ReputationChange = 10 },
                    new() { ChoiceId = 3, Text = "event.cyber.choice3", Cost = 0, OutcomeDescription = "event.cyber.choice3.desc", MoneyChange = -45000, ReputationChange = -15 }
                },
                30000
            )
        };

        private void GenerateRandomEvents()
        {
            if (_random.NextDouble() >= GameConstants.EventGenerationChance) return;

            var players = _playerService.GetAllPlayers().Where(p => !p.IsAI && !p.IsBankrupt).ToList();
            if (!players.Any()) return;

            var targetPlayer = players[_random.Next(players.Count)];
            
            // Don't give too many pending events
            if (targetPlayer.PendingEvents.Count >= 3) return;

            // SMART WEALTH FILTERING
            var validTemplates = EventTemplates.Where(t => targetPlayer.Money >= t.MinMoney).ToList();
            if (!validTemplates.Any()) return;

            var template = validTemplates[_random.Next(validTemplates.Count)];
            
            var gameEvent = new GameEvent
            {
                Title = template.Title,
                Description = template.Description,
                Choices = template.Choices.Select(c => new EventChoice
                {
                    ChoiceId = c.ChoiceId,
                    Text = c.Text,
                    Cost = c.Cost,
                    OutcomeDescription = c.OutcomeDescription,
                    MoneyChange = c.MoneyChange,
                    ReputationChange = c.ReputationChange,
                    ItemReward = c.ItemReward,
                    ItemQuantity = c.ItemQuantity
                }).ToList(),
                ExpiresAt = CurrentTime.AddMinutes(GameConstants.EventExpirationMinutes),
                TargetPlayerId = targetPlayer.Id
            };

            targetPlayer.PendingEvents.Add(gameEvent);
            LogActivity($"New event for {targetPlayer.Name}: {gameEvent.Title}");
        }

        private void ProcessExpiredEvents()
        {
            foreach (var player in _playerService.GetAllPlayers())
            {
                var expiredEvents = player.PendingEvents.Where(e => CurrentTime > e.ExpiresAt && !e.IsExpired).ToList();
                foreach (var evt in expiredEvents)
                {
                    evt.IsExpired = true;
                    evt.OutcomeMessage = "Event expired - no action taken.";
                    // Could apply default negative outcome here
                }
                
                // Clean up old expired events
                player.PendingEvents.RemoveAll(e => e.IsExpired && CurrentTime > e.ExpiresAt.AddMinutes(30));
            }
        }

        public string RespondToEvent(Player player, Guid eventId, int choiceId)
        {
            if (player == null) return "Player not found!";

            var gameEvent = player.PendingEvents.FirstOrDefault(e => e.EventId == eventId);
            if (gameEvent == null) return "Event not found!";
            if (gameEvent.IsExpired) return "This event has expired!";

            var choice = gameEvent.Choices.FirstOrDefault(c => c.ChoiceId == choiceId);
            if (choice == null) return "Invalid choice!";

            // Check if player can afford the choice
            if (choice.Cost.HasValue && player.Money < choice.Cost.Value)
                return $"Not enough money! Need {choice.Cost.Value:C}";

            // Apply effects
            if (choice.MoneyChange.HasValue)
                player.Money += choice.MoneyChange.Value;

            if (choice.ReputationChange.HasValue)
            {
                if (choice.ReputationChange.Value > 0)
                    AwardReputation(player, choice.ReputationChange.Value);
                else
                    PenalizeReputation(player, -choice.ReputationChange.Value);
            }

            if (!string.IsNullOrEmpty(choice.ItemReward) && choice.ItemQuantity.HasValue)
            {
                var marketItem = _exchange.Items.FirstOrDefault(i => i.Name == choice.ItemReward);
                if (marketItem != null)
                {
                    player.Inventory.AddOrUpdateItem(choice.ItemReward, marketItem.CurrentPrice, choice.ItemQuantity.Value);
                }
            }

            gameEvent.IsExpired = true;
            gameEvent.OutcomeMessage = choice.OutcomeDescription;

            LogActivity($"{player.Name} responded to '{gameEvent.Title}': {choice.Text}");

            OnStateChanged?.Invoke();
            return choice.OutcomeDescription;
        }

        public List<GameEvent> GetPlayerEvents(Player player)
        {
            return player?.PendingEvents.Where(e => !e.IsExpired).ToList() ?? new List<GameEvent>();
        }

        #endregion

        #region Bar System

        // --- Bar cash desk & betting limits (economy rebalance) ---
        // The bar is a business with a finite cash desk, not an infinite money printer.
        public decimal BarBankroll { get; set; } = GameConstants.BarBankrollInitial;

        /// <summary>
        /// Set by PokerService while a hand is being played.
        /// Saving is blocked mid-hand (design decision: no save-scumming at the poker table).
        /// </summary>
        public bool PokerHandInProgress { get; set; }

        /// <summary>
        /// The bar keeps business hours (18:00–02:00): no all-day gambling marathons.
        /// </summary>
        public bool IsBarOpen =>
            CurrentTime.Hour >= GameConstants.BarOpenHour || CurrentTime.Hour < GameConstants.BarCloseHour;

        public string BarClosedMessage =>
            $"🔒 Бар закрыт! Работает с {GameConstants.BarOpenHour}:00 до 0{GameConstants.BarCloseHour}:00. Сейчас {CurrentTime:HH:mm} — займись делом, торговец!";

        /// <summary>
        /// Dynamic max bet: capped both by the absolute table limit and by 10% of the player's net worth.
        /// </summary>
        public decimal GetMaxBet(Player player)
        {
            if (player == null) return GameConstants.GamblingMinBet;
            var wealthCap = Math.Floor(player.NetWorth * GameConstants.MaxBetShareOfNetWorth);
            var cap = Math.Min(GameConstants.GamblingMaxBet, wealthCap);
            return Math.Max(GameConstants.GamblingMinBet, cap);
        }

        private void SyncBarDay(Player player)
        {
            if (player.BarWinningsDate != CurrentTime.Date)
            {
                player.BarWinningsDate = CurrentTime.Date;
                player.BarWinningsToday = 0m;
            }
        }

        public decimal GetRemainingDailyWinCap(Player player)
        {
            SyncBarDay(player);
            return Math.Max(0m, GameConstants.BarDailyWinCap - player.BarWinningsToday);
        }

        /// <summary>
        /// Central bet validation for ALL bar games (engine-side and UI-side).
        /// Returns null when the bet is fine, otherwise a user-facing message.
        /// </summary>
        public string? ValidateBet(Player player, decimal bet)
        {
            if (player == null) return "Игрок не найден!";
            if (!IsBarOpen) return BarClosedMessage;
            if (bet < GameConstants.GamblingMinBet)
                return $"Минимальная ставка: {GameConstants.GamblingMinBet:C}";

            var maxBet = GetMaxBet(player);
            if (bet > maxBet)
                return $"Максимальная ставка для тебя сейчас: {maxBet:C} (не больше 10% от капитала и лимита стола)";

            if (player.Money < bet)
                return $"Не хватает денег! У тебя {player.Money:C}";

            if (BarBankroll <= 0m)
                return "Касса бара пуста — сегодня выплат больше не будет. Приходи завтра! 🍺";

            if (GetRemainingDailyWinCap(player) <= 0m)
                return "Бармен разводит руками: «На сегодня хватит, чемпион. Приходи завтра».";

            return null;
        }

        /// <summary>
        /// Settles a gambling result through the bar's cash desk.
        /// net &gt; 0 — player's net win (clamped by the bankroll and the daily win cap);
        /// net &lt; 0 — player's loss (feeds the bankroll back).
        /// Returns the actually paid/charged amount and an optional note when a win was clamped.
        /// </summary>
        public (decimal actualNet, string? note) SettleGambling(Player player, decimal net)
        {
            if (player == null) return (0m, null);
            SyncBarDay(player);

            if (net <= 0m)
            {
                player.Money += net;          // net is negative — player pays
                BarBankroll -= net;           // ...and the bar's cash desk grows
                player.BarWinningsToday += net; // losses free up daily win headroom
                return (net, null);
            }

            var payable = Math.Min(net, Math.Min(GetRemainingDailyWinCap(player), BarBankroll));
            player.Money += payable;
            BarBankroll -= payable;
            player.BarWinningsToday += payable;

            string? note = payable < net
                ? $"Бар смог выплатить только {payable:C} из {net:C} — касса пуста!"
                : null;
            return (payable, note);
        }

        /// <summary>
        /// Daily refill of the bar's cash desk (called on the night rollover).
        /// </summary>
        private void RefillBarBankroll()
        {
            if (BarBankroll < GameConstants.BarBankrollInitial)
            {
                BarBankroll = Math.Min(GameConstants.BarBankrollInitial, BarBankroll + GameConstants.BarBankrollDailyRefill);
            }
        }

        private static readonly string[] BarRumors = {
            "Слышал, что {0} скоро подорожает...",
            "Говорят, скоро будет дефицит {0}!",
            "Знающие люди скупают {0}...",
            "Ожидается обвал цен на {0}!",
            "Инсайдеры сливают {0} — готовься!"
        };

        // NOTE: Order of this array MUST match the BarEncounterType enum order.
        // UI overrides the service-side string with a localized version based on the enum value.
        private static readonly string[] BarEncounters = {
            "Подсел какой-то мужик, предлагает выпить вместе.",
            "Бармен рассказывает истории о рынке.",
            "Заметил знакомого торговца в углу.",
            "Рядом шумная компания обсуждает сделки."
        };

        private void ProcessIntoxication(Player player)
        {
            if (player.SoberUpTime.HasValue && CurrentTime >= player.SoberUpTime.Value)
            {
                player.IntoxicationLevel = 0;
                player.SoberUpTime = null;
            }
        }

        public string GetIntoxicationStatus(Player player)
        {
            return player.IntoxicationLevel switch
            {
                0 => "Трезвый",
                1 => "Слегка выпил",
                2 => "Навеселе",
                3 => "Пьяный",
                _ => "В хлам 🍺"
            };
        }

        public (string result, string? rumorItem) OrderDrink(Player player, string drinkType)
        {
            if (player == null) return ("Игрок не найден!", null);
            if (!IsBarOpen) return (BarClosedMessage, null);

            var price = drinkType switch
            {
                "beer" => GameConstants.BeerPrice,
                "whiskey" => GameConstants.WhiskeyPrice,
                "cocktail" => GameConstants.CocktailPrice,
                _ => GameConstants.BeerPrice
            };

            if (player.Money < price)
                return ($"Не хватает денег! Нужно {price:C}", null);

            player.Money -= price;
            player.IntoxicationLevel++;
            player.LastBarVisit = CurrentTime;
            player.SoberUpTime = CurrentTime.AddMinutes(GameConstants.SoberUpMinutes * player.IntoxicationLevel);

            var drinkName = drinkType switch
            {
                "beer" => "пиво 🍺",
                "whiskey" => "виски 🥃",
                "cocktail" => "коктейль 🍹",
                _ => "напиток"
            };

            string? rumorItem = null;

            // Chance to hear a rumor
            if (_random.NextDouble() < GameConstants.BarEncounterChance)
            {
                var item = _exchange.Items[_random.Next(_exchange.Items.Count)];
                var rumor = BarRumors[_random.Next(BarRumors.Length)];
                rumorItem = item.Name;
                
                LogActivity($"{player.Name} выпил {drinkName} и услышал слух о {item.Name}");
                return ($"Ты заказал {drinkName}. {string.Format(rumor, item.Name)}", rumorItem);
            }

            LogActivity($"{player.Name} выпил {drinkName} в баре");
            OnStateChanged?.Invoke();
            return ($"Ты заказал {drinkName}. Приятного отдыха!", null);
        }

        public string PlayGambling(Player player, decimal bet)
        {
            var betError = ValidateBet(player, bet);
            if (betError != null) return betError;

            // Drunk players make worse decisions
            var winChance = 0.45; // Base 45% chance
            if (player.IntoxicationLevel >= 3)
            {
                winChance -= 0.15; // Drunk = worse odds
            }
            else if (player.IntoxicationLevel >= 1)
            {
                winChance -= 0.05; // Tipsy = slightly worse
            }

            if (_random.NextDouble() < winChance)
            {
                // Win! Double the bet
                var (paid, note) = SettleGambling(player, bet);
                LogActivity($"{player.Name} выиграл {paid:C} в баре!");
                OnStateChanged?.Invoke();
                return $"🎉 Победа! Ты выиграл {paid:C}! Теперь у тебя {player.Money:C}" + (note != null ? $" ({note})" : "");
            }
            else
            {
                // Lose
                SettleGambling(player, -bet);
                LogActivity($"{player.Name} проиграл {bet:C} в баре");
                OnStateChanged?.Invoke();
                return $"😢 Проигрыш! Ты потерял {bet:C}. Осталось {player.Money:C}";
            }
        }

        /// <summary>
        /// Dice game - bet on high (8-12), low (2-6), or lucky 7
        /// </summary>
        public string PlayDice(Player player, decimal bet, string betType)
        {
            var betError = ValidateBet(player, bet);
            if (betError != null) return betError;

            // Roll two dice
            var die1 = _random.Next(1, 7);
            var die2 = _random.Next(1, 7);
            var total = die1 + die2;

            // Drunk penalty - might misread dice
            if (player.IntoxicationLevel >= 3 && _random.NextDouble() < 0.2)
            {
                total = _random.Next(2, 13); // Random result when very drunk
            }

            bool won = false;
            decimal multiplier = 0;
            string betDescription = "";

            switch (betType.ToLower())
            {
                case "high":
                    won = total >= 8 && total <= 12;
                    multiplier = 2m;
                    betDescription = "высокое (8-12)";
                    break;
                case "low":
                    won = total >= 2 && total <= 6;
                    multiplier = 2m;
                    betDescription = "низкое (2-6)";
                    break;
                case "seven":
                    won = total == 7;
                    multiplier = 5m;
                    betDescription = "счастливая семёрка";
                    break;
                default:
                    return "Неверный тип ставки! Выбери: high, low или seven";
            }

            if (won)
            {
                var winnings = bet * (multiplier - 1);
                var (paid, note) = SettleGambling(player, winnings);
                LogActivity($"{player.Name} выиграл {paid:C} в кости (выпало {total})!");
                OnStateChanged?.Invoke();
                return $"🎲🎲 Выпало {die1} + {die2} = {total}! Ты поставил на {betDescription} и выиграл {paid:C}! 🎉" + (note != null ? $" ({note})" : "");
            }
            else
            {
                SettleGambling(player, -bet);
                LogActivity($"{player.Name} проиграл {bet:C} в кости (выпало {total})");
                OnStateChanged?.Invoke();
                return $"🎲🎲 Выпало {die1} + {die2} = {total}. Ты поставил на {betDescription}... Проигрыш {bet:C} 😢";
            }
        }

        /// <summary>
        /// Simplified Blackjack - get cards, try to beat dealer
        /// </summary>
        public string PlayBlackjack(Player player, decimal bet)
        {
            var betError = ValidateBet(player, bet);
            if (betError != null) return betError;

            // Simplified blackjack - draw cards for player and dealer
            int DrawCard() => Math.Min(_random.Next(1, 14), 10); // 1-10, face cards = 10
            
            var playerCard1 = DrawCard();
            var playerCard2 = DrawCard();
            var playerTotal = playerCard1 + playerCard2;
            
            // Player automatically hits if < 12
            if (playerTotal < 12)
            {
                playerTotal += DrawCard();
            }
            
            // Drunk players might hit when they shouldn't
            if (player.IntoxicationLevel >= 2 && playerTotal < 17 && _random.NextDouble() < 0.4)
            {
                playerTotal += DrawCard();
            }

            var dealerCard1 = DrawCard();
            var dealerCard2 = DrawCard();
            var dealerTotal = dealerCard1 + dealerCard2;
            
            // Dealer hits on 16 or less
            while (dealerTotal < 17)
            {
                dealerTotal += DrawCard();
            }

            string resultEmoji;
            string resultText;
            
            if (playerTotal > 21)
            {
                // Player busts
                SettleGambling(player, -bet);
                resultEmoji = "💥";
                resultText = $"Перебор! У тебя {playerTotal}. Проигрыш {bet:C}";
                LogActivity($"{player.Name} проиграл {bet:C} в блэкджек (перебор)");
            }
            else if (dealerTotal > 21)
            {
                // Dealer busts
                var (paidDealerBust, noteDealerBust) = SettleGambling(player, bet);
                resultEmoji = "🎉";
                resultText = $"Дилер перебрал ({dealerTotal})! Ты выиграл {paidDealerBust:C}!" + (noteDealerBust != null ? $" ({noteDealerBust})" : "");
                LogActivity($"{player.Name} выиграл {paidDealerBust:C} в блэкджек!");
            }
            else if (playerTotal > dealerTotal)
            {
                // Player wins
                var (paidWin, noteWin) = SettleGambling(player, bet);
                resultEmoji = "🃏";
                resultText = $"Ты: {playerTotal}, Дилер: {dealerTotal}. Победа! +{paidWin:C}" + (noteWin != null ? $" ({noteWin})" : "");
                LogActivity($"{player.Name} выиграл {paidWin:C} в блэкджек!");
            }
            else if (playerTotal < dealerTotal)
            {
                // Dealer wins
                SettleGambling(player, -bet);
                resultEmoji = "😢";
                resultText = $"Ты: {playerTotal}, Дилер: {dealerTotal}. Проигрыш {bet:C}";
                LogActivity($"{player.Name} проиграл {bet:C} в блэкджек");
            }
            else
            {
                // Push - tie
                resultEmoji = "🤝";
                resultText = $"Ничья! Оба по {playerTotal}. Ставка возвращена.";
            }

            OnStateChanged?.Invoke();
            return $"{resultEmoji} {resultText}";
        }

        /// <summary>
        /// Roulette - bet on red/black or specific number (0-36)
        /// </summary>
        public string PlayRoulette(Player player, decimal bet, string betType, int? number = null)
        {
            var betError = ValidateBet(player, bet);
            if (betError != null) return betError;

            // Spin the wheel (0-36)
            var result = _random.Next(0, 37);
            
            // Red numbers in European roulette
            int[] redNumbers = { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
            bool isRed = redNumbers.Contains(result);
            bool isBlack = result != 0 && !isRed;
            string colorEmoji = result == 0 ? "🟢" : (isRed ? "🔴" : "⚫");

            bool won = false;
            decimal multiplier = 0;
            string betDescription = "";

            switch (betType.ToLower())
            {
                case "red":
                    won = isRed;
                    multiplier = 2m;
                    betDescription = "красное 🔴";
                    break;
                case "black":
                    won = isBlack;
                    multiplier = 2m;
                    betDescription = "чёрное ⚫";
                    break;
                case "number":
                    if (!number.HasValue || number < 0 || number > 36)
                        return "Укажи номер от 0 до 36!";
                    won = result == number.Value;
                    multiplier = 36m;
                    betDescription = $"номер {number.Value}";
                    break;
                default:
                    return "Неверный тип ставки! Выбери: red, black или number";
            }

            // Drunk players might bet on wrong color
            if (player.IntoxicationLevel >= 3 && betType.ToLower() != "number" && _random.NextDouble() < 0.15)
            {
                won = !won; // Confusion!
            }

            if (won)
            {
                var winnings = bet * (multiplier - 1);
                var (paid, note) = SettleGambling(player, winnings);
                LogActivity($"{player.Name} выиграл {paid:C} в рулетку (выпало {result})!");
                OnStateChanged?.Invoke();
                return $"🎰 Выпало {colorEmoji} {result}! Ты поставил на {betDescription} и выиграл {paid:C}! 🎉" + (note != null ? $" ({note})" : "");
            }
            else
            {
                SettleGambling(player, -bet);
                LogActivity($"{player.Name} проиграл {bet:C} в рулетку (выпало {result})");
                OnStateChanged?.Invoke();
                return $"🎰 Выпало {colorEmoji} {result}. Ты поставил на {betDescription}... Проигрыш {bet:C} 😢";
            }
        }

        public (string encounter, bool hasSpecialDeal, string? dealItem, decimal? dealPrice, int? dealQuantity, BarEncounterType? encounterType) TryMeetSomeone(Player player)
        {
            if (player == null) return ("Игрок не найден!", false, null, null, null, null);
            if (!IsBarOpen) return (BarClosedMessage, false, null, null, null, null);

            if (player.IntoxicationLevel == 0)
                return ("Ты трезвый. Закажи что-нибудь, чтобы завести разговор!", false, null, null, null, null);

            var idx = _random.Next(BarEncounters.Length);
            var encounter = BarEncounters[idx];
            var encType = (BarEncounterType)idx;

            // More drunk = more likely to get a deal (but maybe worse)
            var dealChance = 0.2 + (player.IntoxicationLevel * 0.1); // 30-50%+

            if (_random.NextDouble() < dealChance)
            {
                var item = _exchange.Items[_random.Next(_exchange.Items.Count)];
                var quantity = _random.Next(20, 100);

                // Discount based on intoxication (but drunk = riskier deals)
                var discountFactor = player.IntoxicationLevel >= 3
                    ? _random.NextDouble() * 0.5 + 0.3  // 30-80% = could be bad or good
                    : 0.5 + _random.NextDouble() * 0.3; // 50-80% = usually good

                var dealPrice = item.CurrentPrice * (decimal)discountFactor;

                return (
                    $"{encounter} Он предлагает {quantity} {item.Name} по {dealPrice:C} за штуку!",
                    true,
                    item.Name,
                    dealPrice,
                    quantity,
                    encType
                );
            }

            return (encounter, false, null, null, null, encType);
        }

        /// <summary>
        /// Scam flavor texts used when the stranger stiffs the player after being bought a drink.
        /// Returned as a <see cref="BarScamType"/> so the UI can localize without touching the service.
        /// </summary>
        private static readonly BarScamType[] ScamOutcomes = {
            BarScamType.DrinkAndLeave,
            BarScamType.BoringStory,
            BarScamType.BathroomExit,
        };

        /// <summary>
        /// Buy a drink for the stranger who just sat down at your table.
        /// Costs StrangerDrinkPrice and bumps your own intoxication.
        /// 50/50 coin flip: half the time it pays off (deal or rumor),
        /// half the time the stranger just takes the drink and leaves (no reward).
        /// </summary>
        public (string result, string? rumorItem, bool hasSpecialDeal, string? dealItem, decimal? dealPrice, int? dealQuantity, BarScamType? scamType, bool isStolen) BuyDrinkForStranger(Player player)
        {
            if (player == null) return ("Игрок не найден!", null, false, null, null, null, null, false);
            if (!IsBarOpen) return (BarClosedMessage, null, false, null, null, null, null, false);

            var price = GameConstants.StrangerDrinkPrice;
            if (player.Money < price)
                return ($"Не хватает денег! Нужно {price:C}", null, false, null, null, null, null, false);

            player.Money -= price;
            // Drinking together bumps your own intoxication a level
            player.IntoxicationLevel++;
            player.LastBarVisit = CurrentTime;
            player.SoberUpTime = CurrentTime.AddMinutes(GameConstants.SoberUpMinutes * player.IntoxicationLevel);

            // 50/50 — half the time he's legit, half the time he's a conman who just takes the drink.
            if (_random.NextDouble() < 0.5)
            {
                // LEGIT: within the reward branch, split between deal and rumor.
                // Bias toward deal as intoxication rises (he opens up).
                var dealChance = 0.4 + (player.IntoxicationLevel * 0.1); // 50-70%
                if (_random.NextDouble() < dealChance)
                {
                    bool isStolen = _random.NextDouble() < 0.3;
                    var item = _exchange.Items[_random.Next(_exchange.Items.Count)];
                    var quantity = _random.Next(20, 100);
                    double discountFactor;
                    if (isStolen)
                    {
                        discountFactor = 0.3 + _random.NextDouble() * 0.1; // 30-40% of market price (60-70% discount)
                    }
                    else
                    {
                        discountFactor = 0.5 + _random.NextDouble() * 0.3; // 50-80% of market price (20-50% discount)
                    }
                    var dealPrice = item.CurrentPrice * (decimal)discountFactor;

                    LogActivity($"{player.Name} угостил незнакомца выпивкой и получил предложение на {(isStolen ? "ворованный " : "")}{item.Name}");
                    OnStateChanged?.Invoke();
                    return (
                        isStolen 
                            ? $"Ты угостил его выпивкой. Он воровато огляделся и прошептал: «Слушай, есть горячие краденые {item.Name}! Отдам {quantity} шт. с огромной скидкой — всего по {dealPrice:C} за штуку!»"
                            : $"Ты угостил его выпивкой. Он расслабился и предлагает {quantity} {item.Name} по {dealPrice:C} за штуку!",
                        null,
                        true,
                        item.Name,
                        dealPrice,
                        quantity,
                        null,
                        isStolen
                    );
                }
                else
                {
                    var item = _exchange.Items[_random.Next(_exchange.Items.Count)];
                    var rumor = BarRumors[_random.Next(BarRumors.Length)];
                    LogActivity($"{player.Name} угостил незнакомца выпивкой и услышал слух о {item.Name}");
                    OnStateChanged?.Invoke();
                    return (
                        $"Ты угостил его выпивкой. Он наклонился ближе и прошептал: «{string.Format(rumor, item.Name)}»",
                        item.Name,
                        false,
                        null,
                        null,
                        null,
                        null,
                        false
                    );
                }
            }
            else
            {
                // SCAM: the stranger just pockets the drink. Player ate the StrangerDrinkPrice.
                var scam = ScamOutcomes[_random.Next(ScamOutcomes.Length)];
                LogActivity($"{player.Name} угостил незнакомца выпивкой, но остался ни с чем ({scam})");
                OnStateChanged?.Invoke();
                return ("", null, false, null, null, null, scam, false);
            }
        }

        public (string result, bool success, bool isPoliceRaid, bool bountyGiven, decimal bountyAmount) AcceptBarDeal(
            Player player, string itemName, decimal pricePerUnit, int quantity, bool isStolen = false)
        {
            if (player == null) return ("Игрок не найден!", false, false, false, 0);

            var totalCost = pricePerUnit * quantity;
            if (player.Money < totalCost)
                return ($"Не хватает денег! Нужно {totalCost:C}", false, false, false, 0);

            player.Money -= totalCost;

            if (isStolen)
            {
                // POLICE RAID! Confiscation occurs.
                // Find market value of the goods (current standard price * quantity)
                var exchangeItem = _exchange.Items.FirstOrDefault(i => i.Name == itemName);
                var currentPrice = exchangeItem?.CurrentPrice ?? pricePerUnit;
                var marketValue = currentPrice * quantity;
                var bounty = Math.Round(marketValue * 0.1m, 2);

                bool bountyGiven = _random.NextDouble() < 0.5;
                if (bountyGiven)
                {
                    player.Money += bounty;
                    LogActivity($"{player.Name} попал под облаву полиции! Конфискован ворованный товар {itemName} в количестве {quantity} шт., но выплачена награда {bounty:C}");
                    OnStateChanged?.Invoke();
                    return ($"Облава полиции! Товар конфискован, получена награда {bounty:C}", true, true, true, bounty);
                }
                else
                {
                    LogActivity($"{player.Name} попал под облаву полиции! Конфискован ворованный товар {itemName} в количестве {quantity} шт. Награда не выплачена.");
                    OnStateChanged?.Invoke();
                    return ("Облава полиции! Товар конфискован без какой-либо награды.", true, true, false, 0);
                }
            }
            else
            {
                player.Inventory.AddOrUpdateItem(itemName, pricePerUnit, quantity);
                LogActivity($"{player.Name} купил {quantity} {itemName} по сделке в баре за {totalCost:C}");
                OnStateChanged?.Invoke();
                return ($"Сделка! Купил {quantity} {itemName} за {totalCost:C}", true, false, false, 0);
            }
        }

        #endregion

        #region Housing System

        public static readonly List<(PropertyType Type, string Name, string Emoji, decimal Price, decimal Rent, int Capacity, decimal BirthdayBonus)> AvailableProperties = new()
        {
            (PropertyType.SmallRoom, "Маленькая комната", "🛏️", GameConstants.SmallRoomPrice, GameConstants.SmallRoomRent, GameConstants.SmallRoomCapacity, GameConstants.SmallRoomBirthdayBonus),
            (PropertyType.Apartment, "Квартира", "🏢", GameConstants.ApartmentPrice, GameConstants.ApartmentRent, GameConstants.ApartmentCapacity, GameConstants.ApartmentBirthdayBonus),
            (PropertyType.House, "Дом", "🏡", GameConstants.HousePrice, GameConstants.HouseRent, GameConstants.HouseCapacity, GameConstants.HouseBirthdayBonus),
            (PropertyType.Mansion, "Особняк", "🏰", GameConstants.MansionPrice, GameConstants.MansionRent, GameConstants.MansionCapacity, GameConstants.MansionBirthdayBonus)
        };

        public string BuyProperty(Player player, PropertyType propertyType)
        {
            if (player == null) return "Игрок не найден!";

            var propertyInfo = AvailableProperties.FirstOrDefault(p => p.Type == propertyType);
            if (propertyInfo == default)
                return "Такой недвижимости не существует!";

            if (player.Money < propertyInfo.Price)
                return $"Не хватает денег! Нужно {propertyInfo.Price:C}, у тебя {player.Money:C}";

            player.Money -= propertyInfo.Price;
            var newProperty = new Property
            {
                Type = propertyType,
                Name = propertyInfo.Name,
                PurchasePrice = propertyInfo.Price,
                MonthlyRent = propertyInfo.Rent,
                PurchaseDate = CurrentTime,
                LastRentPaid = CurrentTime,
                GuestCapacity = propertyInfo.Capacity,
                BirthdayGiftBonus = propertyInfo.BirthdayBonus
            };
            player.Properties.Add(newProperty);

            LogActivity($"{player.Name} купил {propertyInfo.Emoji} {propertyInfo.Name} за {propertyInfo.Price:C}");
            OnStateChanged?.Invoke();
            return $"Поздравляем! Ты купил {propertyInfo.Emoji} {propertyInfo.Name}!";
        }

        public string SellProperty(Player player, Property property)
        {
            if (player == null) return "Игрок не найден!";
            if (property == null) return "Недвижимость не найдена!";
            if (!player.Properties.Contains(property)) return "У тебя нет этой недвижимости!";

            // Sell for 70% of original price
            var sellPrice = property.PurchasePrice * 0.7m;
            var propertyName = property.Name;

            player.Money += sellPrice;
            player.Properties.Remove(property);

            LogActivity($"{player.Name} продал {propertyName} за {sellPrice:C}");
            OnStateChanged?.Invoke();
            return $"Продал {propertyName} за {sellPrice:C} (70% от цены покупки)";
        }

        public string SellProperty(Player player)
        {
            if (player == null) return "Игрок не найден!";
            var property = player.Properties.FirstOrDefault();
            if (property == null) return "У тебя нет недвижимости для продажи!";
            return SellProperty(player, property);
        }

        /// <summary>
        /// Homeless penalty: a player (or bot) without any housing sleeps in the car.
        /// Reputation drips away nightly, and there's a chance thieves hit the car —
        /// they take a cut of the cash in the glovebox. Applies equally to humans and AI.
        /// </summary>
        private void ProcessHomelessNight(Player player)
        {
            if (player.Properties.Any()) return;
            if (player.IsBankrupt) return;

            // Reputation slowly erodes — nobody respects a trader who sleeps in a parking lot
            if (player.Reputation > GameConstants.HomelessReputationFloor)
            {
                player.Reputation = Math.Max(GameConstants.HomelessReputationFloor,
                    player.Reputation - GameConstants.HomelessReputationLossPerDay);
                if (!player.IsAI)
                {
                    LogActivity($"🚗 {player.Name} провёл ночь в машине. Репутация страдает (−{GameConstants.HomelessReputationLossPerDay}). Может, пора снять хотя бы комнату?");
                }
            }

            // Thieves prowl parking lots at night
            if (player.Money > 0 && _random.NextDouble() < GameConstants.HomelessTheftChancePerNight)
            {
                var stolen = Math.Min(Math.Round(player.Money * GameConstants.HomelessTheftCashPercent, 2),
                    GameConstants.HomelessTheftCashCap);
                if (stolen > 0)
                {
                    player.Money -= stolen;
                    if (!player.IsAI)
                    {
                        LogActivity($"🥷 Ночью машину {player.Name} вскрыли! Из бардачка пропало {stolen:C}. Дом с дверью и замком решил бы проблему...");
                    }
                }
            }
        }

        public void ProcessRent(Player player)
        {
            foreach (var property in player.Properties.ToList())
            {
                var daysSinceLastRent = (CurrentTime - property.LastRentPaid).Days;
                if (daysSinceLastRent >= GameConstants.RentDueGameDays)
                {
                    // Rent is due!
                    if (player.Money >= property.MonthlyRent)
                    {
                        player.Money -= property.MonthlyRent;
                        property.LastRentPaid = CurrentTime;
                        LogActivity($"{player.Name} заплатил аренду {property.MonthlyRent:C} за {property.Name}");
                    }
                    else
                    {
                        // Can't pay rent - lose property!
                        LogActivity($"{player.Name} не смог заплатить аренду и потерял {property.Name}!");
                        player.Properties.Remove(property);
                    }
                }
            }
        }

        public string GetPropertyStatus(Player player)
        {
            if (player == null || !player.Properties.Any())
                return "Бездомный 🚗 (спишь в машине)";

            if (player.Properties.Count == 1)
            {
                var p = player.Properties[0];
                var info = AvailableProperties.FirstOrDefault(x => x.Type == p.Type);
                var emoji = info != default ? info.Emoji : "🏡";
                return $"{emoji} {p.Name}";
            }

            return $"🏡 Недвижимость ({player.Properties.Count} шт.)";
        }

        public (int guestCount, decimal giftAmount) CalculateBirthdayGifts(Player player)
        {
            if (player == null || !player.Properties.Any())
            {
                // Homeless - minimal gifts
                return (1, 20m);
            }

            int maxGuests = player.Properties.Sum(p => p.GuestCapacity);
            var guestCount = _random.Next(1, maxGuests + 1);
            var totalBonus = player.Properties.Sum(p => p.BirthdayGiftBonus);
            var totalGifts = totalBonus + (guestCount * 10m); // Extra per guest

            return (guestCount, totalGifts);
        }

        #endregion
    }
}

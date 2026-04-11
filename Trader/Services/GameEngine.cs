using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using EconomicGame.Configuration;
using EconomicGame.Extensions;
using EconomicGame.Hubs;
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
        private readonly IHubContext<GameHub> _hubContext;
        private readonly PlayerService _playerService;
        private readonly SyncEngine _syncEngine;
        private readonly CorporateRivalryService _rivalryService;
        private readonly CorporateActionService _actionService;
        private readonly InsuranceService _insuranceService;
        private readonly StockMarketService _stockMarketService;
        
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

        public GameEngine(IHubContext<GameHub> hubContext, IConfiguration configuration, PlayerService playerService, SyncEngine syncEngine, CorporateRivalryService rivalryService, CorporateActionService actionService, InsuranceService insuranceService, StockMarketService stockMarketService)
        {
            _market = new Market();
            
            var items = configuration.GetSection("MarketItems").Get<List<MarketItem>>() ?? new List<MarketItem>();
            _exchange = new StockExchange(items);
            
            _bank = new Bank();
            _news = new List<News>();
            _hubContext = hubContext;
            _playerService = playerService;
            _syncEngine = syncEngine;
            _rivalryService = rivalryService;
            _actionService = actionService;
            _insuranceService = insuranceService;
            _stockMarketService = stockMarketService;
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

        public void UpdateGameState()
        {
            var previousTime = CurrentTime;
            CurrentTime = CurrentTime.AddMinutes(15); // Advance time

            // Night Cycle / Collective Intelligence Sync
            if (previousTime.Hour == 23 && CurrentTime.Hour == 0)
            {
                LogActivity("Collective Consciousness Sync Initiated...");
                _syncEngine.PerformNightlySync();
            }

            _exchange.UpdatePrices();
            
            // Stock Market updates
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
            
            // Banking Updates
            _bank.UpdateInterestRate();
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
                
                // Corporate Rivalry updates (don't run every tick)
                if (_random.NextDouble() < 0.05) // 5% chance per tick to recalculate rivals
                {
                    _rivalryService.UpdateRivals();
                }

                // Phase 7: Shadow Operations maintenance
                foreach (var p in _playerService.GetAllPlayers())
                {
                    if (p.IsSabotaged && p.SabotageEndTime.HasValue && CurrentTime >= p.SabotageEndTime.Value)
                    {
                        p.IsSabotaged = false;
                        p.SabotageEndTime = null;
                    }
                }

                if (_random.NextDouble() < 0.02) // 2% chance per tick for subsidiary payouts
                {
                    _actionService.ProcessSubsidaryPayouts();
                }

                _insuranceService.UpdateInsuranceStatus(CurrentTime);

                OnStateChanged?.Invoke();
                // Generate monthly report every N game days
                var currentDay = (int)(CurrentTime - DateTime.Today.AddHours(8)).TotalDays;
                if (currentDay > 0 && currentDay % GameConstants.MonthlyReportDays == 0 && player.LastReportMonth != currentDay)
                {
                    GenerateMonthlyReport(player, currentDay);
                }
            }
            
            // Notify local subscribers (Server-side Blazor components)
            OnStateChanged?.Invoke();

            // Notify external clients (if any)
            _hubContext.Clients.All.SendAsync("PriceUpdate", _exchange.Items);
            _hubContext.Clients.All.SendAsync("NewsUpdate", _news.LastOrDefault());
        }

        #region Logging

        private void LogActivity(string message)
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
            (FactoryType.PharmLab, "Фармацевтическая лаборатория", "💊", GameConstants.PharmLabPrice, GameConstants.PharmLabMaintenance, "Фармацевтика")
        };

        public string BuyVehicle(Player player, VehicleType vehicleType)
        {
            if (player == null) return "Игрок не найден!";

            var vehicleInfo = AvailableVehicles.FirstOrDefault(v => v.Type == vehicleType);
            if (vehicleInfo == default) return "Такого транспорта нет!";

            if (player.Vehicle != null)
            {
                // Trade-in: sell current for 60%
                var tradeInValue = player.Vehicle.PurchasePrice * 0.6m;
                if (player.Money + tradeInValue < vehicleInfo.Price)
                    return $"Не хватает денег! Нужно {vehicleInfo.Price:C}, у тебя {player.Money:C} + trade-in {tradeInValue:C}";
                
                player.Money += tradeInValue;
                LogActivity($"{player.Name} сдал {player.Vehicle.Name} в trade-in за {tradeInValue:C}");
            }

            if (player.Money < vehicleInfo.Price)
                return $"Не хватает денег! Нужно {vehicleInfo.Price:C}";

            player.Money -= vehicleInfo.Price;
            player.Vehicle = new Vehicle
            {
                Type = vehicleType,
                Name = vehicleInfo.Name,
                Emoji = vehicleInfo.Emoji,
                CargoCapacity = vehicleInfo.Capacity,
                PurchasePrice = vehicleInfo.Price,
                IsOperational = true,
                PurchaseDate = CurrentTime
            };

            LogActivity($"{player.Name} купил {vehicleInfo.Emoji} {vehicleInfo.Name} за {vehicleInfo.Price:C}");
            OnStateChanged?.Invoke();
            return $"Поздравляем! {vehicleInfo.Emoji} {vehicleInfo.Name} теперь твой! Вместимость: {vehicleInfo.Capacity} ед.";
        }

        public string SellVehicle(Player player)
        {
            if (player?.Vehicle == null) return "У тебя нет транспорта!";

            var sellPrice = player.Vehicle.PurchasePrice * 0.6m;
            var vehicleName = $"{player.Vehicle.Emoji} {player.Vehicle.Name}";

            player.Money += sellPrice;
            player.Vehicle = null;

            LogActivity($"{player.Name} продал {vehicleName} за {sellPrice:C}");
            OnStateChanged?.Invoke();
            return $"Продал {vehicleName} за {sellPrice:C}";
        }

        public string BuyLand(Player player, LandType landType)
        {
            if (player == null) return "Игрок не найден!";
            if (player.Land != null) return $"У тебя уже есть участок: {player.Land.Name}. Сначала продай его!";

            var landInfo = AvailableLand.FirstOrDefault(l => l.Type == landType);
            if (landInfo == default) return "Такого участка нет!";

            if (player.Money < landInfo.Price)
                return $"Не хватает денег! Нужно {landInfo.Price:C}";

            player.Money -= landInfo.Price;
            player.Land = new Land
            {
                Type = landType,
                Name = landInfo.Name,
                Emoji = landInfo.Emoji,
                PurchasePrice = landInfo.Price,
                PurchaseDate = CurrentTime,
                MaxWarehouseLevel = landInfo.MaxWarehouse
            };

            LogActivity($"{player.Name} купил {landInfo.Emoji} {landInfo.Name} за {landInfo.Price:C}");
            OnStateChanged?.Invoke();
            return $"Поздравляем! {landInfo.Emoji} {landInfo.Name} теперь твой!";
        }

        public string SellLand(Player player)
        {
            if (player?.Land == null) return "У тебя нет земельного участка!";
            if (player.Warehouses.Any()) return "Сначала продай все склады!";

            var sellPrice = player.Land.PurchasePrice * 0.7m;
            var landName = $"{player.Land.Emoji} {player.Land.Name}";

            player.Money += sellPrice;
            player.Land = null;

            LogActivity($"{player.Name} продал {landName} за {sellPrice:C}");
            OnStateChanged?.Invoke();
            return $"Продал {landName} за {sellPrice:C}";
        }

        public string BuyWarehouse(Player player, WarehouseType warehouseType)
        {
            if (player == null) return "Игрок не найден!";
            if (player.Land == null) return "Сначала купи земельный участок!";
            
            // Check warehouse limit based on trading license
            if (player.Warehouses.Count >= player.MaxWarehouses)
                return $"Достигнут лимит складов ({player.MaxWarehouses})! Купи торговую лицензию для расширения.";

            var warehouseInfo = AvailableWarehouses.FirstOrDefault(w => w.Type == warehouseType);
            if (warehouseInfo == default) return "Такого склада нет!";

            if (player.Land.MaxWarehouseLevel < warehouseInfo.RequiredLandLevel)
                return $"Твой участок слишком мал для этого склада! Нужен участок уровня {warehouseInfo.RequiredLandLevel}+";

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
            if (player.Land == null) return "Сначала купи земельный участок!";
            
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
            var remainingCapacity = (player.Vehicle?.CargoCapacity ?? 0) + player.Warehouses.Where(w => w.WarehouseId != warehouse.WarehouseId).Sum(w => w.Capacity);
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
                        var remainingCapacity = (player.Vehicle?.CargoCapacity ?? 0) + 
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
            if (player.AutoProductionRecipes == null || !player.AutoProductionRecipes.Any()) return;
            if (!player.Warehouses.Any()) return;

            foreach (var recipeId in player.AutoProductionRecipes)
            {
                var recipe = ProductionRecipes.AllRecipes.FirstOrDefault(r => r.RecipeId == recipeId);
                if (recipe == null) continue;

                // Check resources
                bool hasResources = true;
                foreach (var input in recipe.Inputs)
                {
                    var invItem = player.Inventory.FirstOrDefault(i => i.ItemName == input.Key);
                    if (invItem == null || invItem.Quantity < input.Value)
                    {
                        hasResources = false;
                        break;
                    }
                }

                if (!hasResources) continue;

                // Check money
                if (player.Money < recipe.ProductionCost) continue;

                // Check storage space
                var outputTotal = recipe.Outputs.Sum(o => o.Value);
                var inputTotal = recipe.Inputs.Sum(i => i.Value);
                var netChange = outputTotal - inputTotal;
                if (player.AvailableCargoSpace < netChange) continue;

                // Execute production
                player.Money -= recipe.ProductionCost;

                // Move inputs
                foreach (var input in recipe.Inputs)
                {
                    player.Inventory.RemoveQuantity(input.Key, input.Value);
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
                    int quantityWithEfficiency = (int)(output.Value * efficiencyMultiplier);
                    player.Inventory.AddOrUpdateItem(output.Key, price, quantityWithEfficiency);
                }

                LogActivity($"[AUTO] {player.Name} произвёл {recipe.Name}");
            }
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

        private void GenerateDisaster()
        {
            // Small chance for a disaster (2% per tick)
            if (_random.NextDouble() >= 0.02) return;

            bool isEarthquake = _random.NextDouble() > 0.5;
            string itemName = isEarthquake ? "Oil" : "Wheat";
            double lossPercent = isEarthquake ? 0.5 : 0.7;
            string disasterName = isEarthquake ? "Землетрясение" : "Наводнение";
            string description = isEarthquake 
                ? "Мощное землетрясение разрушило нефтяные терминалы! 50% запасов нефти потеряно." 
                : "Сильное наводнение затопило зернохранилища! 70% запасов зерна уничтожено.";

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
            
            var totalCost = item.CurrentPrice * quantity;

            if (player.Money < totalCost)
                return $"Not enough money! Need {totalCost:C}, you have {player.Money:C}";

            if (item.AvailableQuantity < quantity)
                return $"Not enough {item.Name} available! Only {item.AvailableQuantity} in stock.";

            // Phase 9: Logistics Constraints
            if (player.Vehicle != null && quantity > player.Vehicle.CargoCapacity)
                return $"Ваш транспорт ({player.Vehicle.Name}) может перевозить не более {player.Vehicle.CargoCapacity} ед. за раз. Совершите несколько поездок или смените транспорт.";

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

        private static readonly (string Title, string Description, List<EventChoice> Choices)[] EventTemplates = {
            (
                "Storm Warning!",
                "A major storm is approaching. It could damage stored goods.",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Buy insurance ($500)", Cost = 500, OutcomeDescription = "Your goods are protected.", MoneyChange = -500 },
                    new() { ChoiceId = 2, Text = "Risk it", Cost = 0, OutcomeDescription = "Let's hope for the best...", MoneyChange = 0 }
                }
            ),
            (
                "Investment Opportunity",
                "A trader offers you an exclusive deal on bulk goods. High risk, high reward!",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Invest $1000", Cost = 1000, OutcomeDescription = "You take the gamble.", MoneyChange = -1000 },
                    new() { ChoiceId = 2, Text = "Pass", Cost = 0, OutcomeDescription = "You play it safe.", MoneyChange = 0 }
                }
            ),
            (
                "Charity Request",
                "A local charity asks for a donation. It would boost your reputation.",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Donate $200", Cost = 200, OutcomeDescription = "Your generosity is appreciated!", MoneyChange = -200, ReputationChange = 10 },
                    new() { ChoiceId = 2, Text = "Decline politely", Cost = 0, OutcomeDescription = "Maybe next time.", MoneyChange = 0, ReputationChange = -2 }
                }
            ),
            (
                "Lucky Find!",
                "You found a valuable item on the street!",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Keep it", Cost = 0, OutcomeDescription = "Finders keepers!", MoneyChange = 300, ReputationChange = -5 },
                    new() { ChoiceId = 2, Text = "Turn it in", Cost = 0, OutcomeDescription = "The owner is grateful.", MoneyChange = 50, ReputationChange = 5 }
                }
            ),
            // Life-sim events
            (
                "Тяжёлая ночь",
                "У тебя нет жилья, пришлось ночевать в машине. Утром чувствуешь себя отвратительно...",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Пойти в больницу ($800)", Cost = 800, OutcomeDescription = "Врач выписал антибиотики. Через пару дней будешь как новенький!", MoneyChange = -800 },
                    new() { ChoiceId = 2, Text = "Перетерпеть", Cost = 0, OutcomeDescription = "Состояние ухудшилось... Пришлось ехать в скорую. Счёт вдвое больше.", MoneyChange = -1500 }
                }
            ),
            (
                "Встреча в баре",
                "Познакомился с мужиком в баре. Выпили по паре кружек. Он предлагает купить 100 единиц зерна по смешной цене!",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Купить! ($200)", Cost = 200, OutcomeDescription = "Сделка! 100 Wheat теперь твои.", MoneyChange = -200, ItemReward = "Wheat", ItemQuantity = 100 },
                    new() { ChoiceId = 2, Text = "Слишком хорошо, отказаться", Cost = 0, OutcomeDescription = "Осторожность не повредит. Он ушёл расстроенный.", MoneyChange = 0, ReputationChange = -1 }
                }
            ),
            (
                "День Рождения! 🎂",
                "Сегодня твой ДР! Живёшь в маленькой комнате, но пришли 2 друга с подарками.",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Устроить вечеринку ($150)", Cost = 150, OutcomeDescription = "Отличный вечер! Друзья скинулись на подарок.", MoneyChange = 100, ReputationChange = 5 },
                    new() { ChoiceId = 2, Text = "Посидеть скромно", Cost = 0, OutcomeDescription = "Тихо посидели с чаем. Подарили немного денег.", MoneyChange = 50, ReputationChange = 2 }
                }
            ),
            (
                "Подозрительный тип",
                "Какой-то тип предлагает 'инвестицию'. Гарантирует 200% через неделю!",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Вложить $500", Cost = 500, OutcomeDescription = "Скорее всего это развод... но вдруг повезёт?", MoneyChange = -500 },
                    new() { ChoiceId = 2, Text = "Отказаться", Cost = 0, OutcomeDescription = "Здравый смысл победил. Он исчез в толпе.", MoneyChange = 0, ReputationChange = 1 }
                }
            ),
            (
                "Случайная работа",
                "Знакомый предлагает подработку на складе. Тяжело, но честно.",
                new List<EventChoice> {
                    new() { ChoiceId = 1, Text = "Поработать (4 часа)", Cost = 0, OutcomeDescription = "Заработал честные деньги и уважение.", MoneyChange = 300, ReputationChange = 3 },
                    new() { ChoiceId = 2, Text = "Отказаться", Cost = 0, OutcomeDescription = "Сегодня не твой день.", MoneyChange = 0 }
                }
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

            var template = EventTemplates[_random.Next(EventTemplates.Length)];
            
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
                    ReputationChange = c.ReputationChange
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

        private static readonly string[] BarRumors = {
            "Слышал, что {0} скоро подорожает...",
            "Говорят, скоро будет дефицит {0}!",
            "Знающие люди скупают {0}...",
            "Ожидается обвал цен на {0}!",
            "Инсайдеры сливают {0} — готовься!"
        };

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
            if (player == null) return "Игрок не найден!";

            if (bet < GameConstants.GamblingMinBet)
                return $"Минимальная ставка: {GameConstants.GamblingMinBet:C}";

            if (bet > GameConstants.GamblingMaxBet)
                return $"Максимальная ставка: {GameConstants.GamblingMaxBet:C}";

            if (player.Money < bet)
                return $"Не хватает денег! У тебя {player.Money:C}";

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
                player.Money += bet;
                LogActivity($"{player.Name} выиграл {bet:C} в баре!");
                OnStateChanged?.Invoke();
                return $"🎉 Победа! Ты выиграл {bet:C}! Теперь у тебя {player.Money:C}";
            }
            else
            {
                // Lose
                player.Money -= bet;
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
            if (player == null) return "Игрок не найден!";

            if (bet < GameConstants.GamblingMinBet)
                return $"Минимальная ставка: {GameConstants.GamblingMinBet:C}";

            if (bet > GameConstants.GamblingMaxBet)
                return $"Максимальная ставка: {GameConstants.GamblingMaxBet:C}";

            if (player.Money < bet)
                return $"Не хватает денег! У тебя {player.Money:C}";

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
                player.Money += winnings;
                LogActivity($"{player.Name} выиграл {winnings:C} в кости (выпало {total})!");
                OnStateChanged?.Invoke();
                return $"🎲🎲 Выпало {die1} + {die2} = {total}! Ты поставил на {betDescription} и выиграл {winnings:C}! 🎉";
            }
            else
            {
                player.Money -= bet;
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
            if (player == null) return "Игрок не найден!";

            if (bet < GameConstants.GamblingMinBet)
                return $"Минимальная ставка: {GameConstants.GamblingMinBet:C}";

            if (bet > GameConstants.GamblingMaxBet)
                return $"Максимальная ставка: {GameConstants.GamblingMaxBet:C}";

            if (player.Money < bet)
                return $"Не хватает денег! У тебя {player.Money:C}";

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
                player.Money -= bet;
                resultEmoji = "💥";
                resultText = $"Перебор! У тебя {playerTotal}. Проигрыш {bet:C}";
                LogActivity($"{player.Name} проиграл {bet:C} в блэкджек (перебор)");
            }
            else if (dealerTotal > 21)
            {
                // Dealer busts
                player.Money += bet;
                resultEmoji = "🎉";
                resultText = $"Дилер перебрал ({dealerTotal})! Ты выиграл {bet:C}!";
                LogActivity($"{player.Name} выиграл {bet:C} в блэкджек!");
            }
            else if (playerTotal > dealerTotal)
            {
                // Player wins
                player.Money += bet;
                resultEmoji = "🃏";
                resultText = $"Ты: {playerTotal}, Дилер: {dealerTotal}. Победа! +{bet:C}";
                LogActivity($"{player.Name} выиграл {bet:C} в блэкджек!");
            }
            else if (playerTotal < dealerTotal)
            {
                // Dealer wins
                player.Money -= bet;
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
            if (player == null) return "Игрок не найден!";

            if (bet < GameConstants.GamblingMinBet)
                return $"Минимальная ставка: {GameConstants.GamblingMinBet:C}";

            if (bet > GameConstants.GamblingMaxBet)
                return $"Максимальная ставка: {GameConstants.GamblingMaxBet:C}";

            if (player.Money < bet)
                return $"Не хватает денег! У тебя {player.Money:C}";

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
                player.Money += winnings;
                LogActivity($"{player.Name} выиграл {winnings:C} в рулетку (выпало {result})!");
                OnStateChanged?.Invoke();
                return $"🎰 Выпало {colorEmoji} {result}! Ты поставил на {betDescription} и выиграл {winnings:C}! 🎉";
            }
            else
            {
                player.Money -= bet;
                LogActivity($"{player.Name} проиграл {bet:C} в рулетку (выпало {result})");
                OnStateChanged?.Invoke();
                return $"🎰 Выпало {colorEmoji} {result}. Ты поставил на {betDescription}... Проигрыш {bet:C} 😢";
            }
        }

        public (string encounter, bool hasSpecialDeal, string? dealItem, decimal? dealPrice, int? dealQuantity) TryMeetSomeone(Player player)
        {
            if (player == null) return ("Игрок не найден!", false, null, null, null);

            if (player.IntoxicationLevel == 0)
                return ("Ты трезвый. Закажи что-нибудь, чтобы завести разговор!", false, null, null, null);

            var encounter = BarEncounters[_random.Next(BarEncounters.Length)];

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
                    quantity
                );
            }

            return (encounter, false, null, null, null);
        }

        public string AcceptBarDeal(Player player, string itemName, decimal pricePerUnit, int quantity)
        {
            if (player == null) return "Игрок не найден!";

            var totalCost = pricePerUnit * quantity;
            if (player.Money < totalCost)
                return $"Не хватает денег! Нужно {totalCost:C}";

            player.Money -= totalCost;
            player.Inventory.AddOrUpdateItem(itemName, pricePerUnit, quantity);

            LogActivity($"{player.Name} купил {quantity} {itemName} по сделке в баре за {totalCost:C}");
            OnStateChanged?.Invoke();
            return $"Сделка! Купил {quantity} {itemName} за {totalCost:C}";
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

            if (player.Property != null)
                return $"У тебя уже есть жильё: {player.Property.Name}. Сначала продай его!";

            var propertyInfo = AvailableProperties.FirstOrDefault(p => p.Type == propertyType);
            if (propertyInfo == default)
                return "Такой недвижимости не существует!";

            if (player.Money < propertyInfo.Price)
                return $"Не хватает денег! Нужно {propertyInfo.Price:C}, у тебя {player.Money:C}";

            player.Money -= propertyInfo.Price;
            player.Property = new Property
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

            LogActivity($"{player.Name} купил {propertyInfo.Emoji} {propertyInfo.Name} за {propertyInfo.Price:C}");
            OnStateChanged?.Invoke();
            return $"Поздравляем! Ты купил {propertyInfo.Emoji} {propertyInfo.Name}!";
        }

        public string SellProperty(Player player)
        {
            if (player == null) return "Игрок не найден!";

            if (player.Property == null)
                return "У тебя нет недвижимости для продажи!";

            // Sell for 70% of original price
            var sellPrice = player.Property.PurchasePrice * 0.7m;
            var propertyName = player.Property.Name;

            player.Money += sellPrice;
            player.Property = null;

            LogActivity($"{player.Name} продал {propertyName} за {sellPrice:C}");
            OnStateChanged?.Invoke();
            return $"Продал {propertyName} за {sellPrice:C} (70% от цены покупки)";
        }

        public void ProcessRent(Player player)
        {
            if (player.Property == null) return;

            var daysSinceLastRent = (CurrentTime - player.Property.LastRentPaid).Days;
            if (daysSinceLastRent >= GameConstants.RentDueGameDays)
            {
                // Rent is due!
                if (player.Money >= player.Property.MonthlyRent)
                {
                    player.Money -= player.Property.MonthlyRent;
                    player.Property.LastRentPaid = CurrentTime;
                    LogActivity($"{player.Name} заплатил аренду {player.Property.MonthlyRent:C} за {player.Property.Name}");
                }
                else
                {
                    // Can't pay rent - lose property!
                    LogActivity($"{player.Name} не смог заплатить аренду и потерял {player.Property.Name}!");
                    player.Property = null;
                }
            }
        }

        public string GetPropertyStatus(Player player)
        {
            if (player?.Property == null)
                return "Бездомный 🚗 (спишь в машине)";

            var propertyInfo = AvailableProperties.FirstOrDefault(p => p.Type == player.Property.Type);
            return $"{propertyInfo.Emoji} {player.Property.Name}";
        }

        public (int guestCount, decimal giftAmount) CalculateBirthdayGifts(Player player)
        {
            if (player?.Property == null)
            {
                // Homeless - minimal gifts
                return (1, 20m);
            }

            var guestCount = _random.Next(1, player.Property.GuestCapacity + 1);
            var baseGift = player.Property.BirthdayGiftBonus;
            var totalGifts = baseGift + (guestCount * 10m); // Extra per guest

            return (guestCount, totalGifts);
        }

        #endregion
    }
}

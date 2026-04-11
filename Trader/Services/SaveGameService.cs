using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EconomicGame;
using EconomicGame.Models;

namespace EconomicGame.Services
{
    public class SaveGameService
    {
        private readonly PlayerService _playerService;
        private readonly GameEngine _gameEngine;
        private readonly StockMarketService _stockMarketService;
        private readonly string _savePath;

        public SaveGameService(PlayerService playerService, GameEngine gameEngine, StockMarketService stockMarketService)
        {
            _playerService = playerService;
            _gameEngine = gameEngine;
            
            // Save to user's AppData
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _savePath = Path.Combine(appData, "Trader", "Saves");
            Directory.CreateDirectory(_savePath);
            _stockMarketService = stockMarketService;
        }

        public List<(string FileName, GameSaveData Data)> GetSaveFiles()
        {
            var saves = new List<(string, GameSaveData)>();
            
            if (!Directory.Exists(_savePath)) return saves;

            foreach (var file in Directory.GetFiles(_savePath, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonSerializer.Deserialize<GameSaveData>(json);
                    if (data != null)
                    {
                        saves.Add((Path.GetFileNameWithoutExtension(file), data));
                    }
                }
                catch { /* Skip corrupted saves */ }
            }

            return saves.OrderByDescending(s => s.Item2.SavedAt).ToList();
        }

        public string SaveGame(string saveName)
        {
            try
            {
                var player = _playerService.GetCurrentPlayer();
                if (player == null)
                    return "❌ Нет активного игрока для сохранения!";

                var saveData = new GameSaveData
                {
                    SaveName = saveName,
                    SavedAt = DateTime.Now,
                    GameTime = _gameEngine.CurrentTime,
                    GameDay = (_gameEngine.CurrentTime - DateTime.Today).Days + 1,
                    PlayerData = ConvertPlayerToSave(player),
                    
                    // Save all AI nodes
                    AIPlayers = _playerService.GetAllPlayers()
                        .Where(p => p.IsAI)
                        .Select(ai => ConvertPlayerToSave(ai))
                        .ToList(),
                        
                    // Save Market State
                    MarketItems = _gameEngine.StockExchange.Items.Select(m => new MarketItemSave
                    {
                        Name = m.Name,
                        CurrentPrice = m.CurrentPrice,
                        PriceHistory = m.PriceHistory.ToList(),
                        BuyVolume = m.BuyVolume,
                        SellVolume = m.SellVolume
                    }).ToList(),

                    // Save Stock Market State
                    Stocks = _stockMarketService.Stocks.Select(s => new StockSaveData
                    {
                        Ticker = s.Ticker,
                        CompanyName = s.CompanyName,
                        SharePrice = s.SharePrice,
                        DividendYield = s.DividendYield,
                        TotalShares = s.TotalShares,
                        AvailableShares = s.AvailableShares,
                        PriceHistory = s.PriceHistory.ToList(),
                        LinkedCommodity = s.LinkedCommodity,
                        CorrelationFactor = s.CorrelationFactor
                    }).ToList(),
                    LastDividendPaid = _stockMarketService.LastDividendPaid
                };

                var json = JsonSerializer.Serialize(saveData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                var fileName = $"{SanitizeFileName(saveName)}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var filePath = Path.Combine(_savePath, fileName);
                File.WriteAllText(filePath, json);

                return $"✅ Игра сохранена: {saveName}";
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка сохранения: {ex.Message}";
            }
        }

        public string LoadGame(string fileName)
        {
            try
            {
                var filePath = Path.Combine(_savePath, fileName + ".json");
                if (!File.Exists(filePath))
                    return "❌ Файл сохранения не найден!";

                var json = File.ReadAllText(filePath);
                var saveData = JsonSerializer.Deserialize<GameSaveData>(json);

                if (saveData?.PlayerData == null)
                    return "❌ Повреждённый файл сохранения!";

                // Clear existing state
                _playerService.ClearAllPlayers();

                // Restore player
                var player = RestorePlayerFromSave(saveData.PlayerData);
                _playerService.SetCurrentPlayer(player);

                // Restore AI Ecosystem
                foreach (var aiSave in saveData.AIPlayers)
                {
                    var ai = RestorePlayerFromSave(aiSave);
                    _playerService.RegisterPlayer(ai);
                }

                // Restore Market State
                foreach (var itemSave in saveData.MarketItems)
                {
                    var marketItem = _gameEngine.StockExchange.Items.FirstOrDefault(i => i.Name == itemSave.Name);
                    if (marketItem != null)
                    {
                        marketItem.CurrentPrice = itemSave.CurrentPrice;
                        marketItem.PriceHistory = itemSave.PriceHistory.ToList();
                        marketItem.BuyVolume = itemSave.BuyVolume;
                        marketItem.SellVolume = itemSave.SellVolume;
                    }
                }

                // Restore game time
                _gameEngine.SetCurrentTime(saveData.GameTime);

                // Restore Stock Market State
                if (saveData.Stocks != null && saveData.Stocks.Any())
                {
                    foreach (var stockSave in saveData.Stocks)
                    {
                        var stock = _stockMarketService.Stocks.FirstOrDefault(s => s.Ticker == stockSave.Ticker);
                        if (stock != null)
                        {
                            stock.SharePrice = stockSave.SharePrice;
                            stock.DividendYield = stockSave.DividendYield;
                            stock.AvailableShares = stockSave.AvailableShares;
                            stock.PriceHistory = stockSave.PriceHistory.ToList();
                        }
                    }
                    _stockMarketService.LastDividendPaid = saveData.LastDividendPaid;
                }

                // Trigger UI refresh for all subscribers
                _gameEngine.TriggerStateChanged();

                return $"✅ Игра загружена: {saveData.SaveName} (День {saveData.GameDay})";
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка загрузки: {ex.Message}";
            }
        }

        public string DeleteSave(string fileName)
        {
            try
            {
                var filePath = Path.Combine(_savePath, fileName + ".json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return "✅ Сохранение удалено";
                }
                return "❌ Файл не найден";
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка удаления: {ex.Message}";
            }
        }

        private PlayerSaveData ConvertPlayerToSave(Player player)
        {
            return new PlayerSaveData
            {
                Id = player.Id,
                Name = player.Name,
                Money = player.Money,
                Reputation = player.Reputation,
                IntoxicationLevel = player.IntoxicationLevel,
                SoberUpTime = player.SoberUpTime,
                
                IsAI = player.IsAI,
                Strategy = player.IsAI ? player.Strategy : null,
                DailyProfit = player.DailyProfit,
                TotalTrades = player.TotalTrades,
                ProfitableTrades = player.ProfitableTrades,
                
                Inventory = player.Inventory.Select(i => new InventoryItemSave
                {
                    ItemName = i.ItemName,
                    AveragePrice = i.AveragePrice,
                    Quantity = i.Quantity
                }).ToList(),

                Vehicle = player.Vehicle != null ? new VehicleSave
                {
                    Type = player.Vehicle.Type,
                    Name = player.Vehicle.Name,
                    CargoCapacity = player.Vehicle.CargoCapacity,
                    PurchasePrice = player.Vehicle.PurchasePrice
                } : null,

                Property = player.Property != null ? new PropertySave
                {
                    Type = player.Property.Type,
                    Name = player.Property.Name,
                    PurchasePrice = player.Property.PurchasePrice,
                    MonthlyRent = player.Property.MonthlyRent,
                    GuestCapacity = player.Property.GuestCapacity,
                    BirthdayGiftBonus = player.Property.BirthdayGiftBonus
                } : null,

                Land = player.Land != null ? new LandSave
                {
                    Type = player.Land.Type,
                    Name = player.Land.Name,
                    PurchasePrice = player.Land.PurchasePrice,
                    MaxWarehouseLevel = player.Land.MaxWarehouseLevel
                } : null,

                Warehouses = player.Warehouses.Select(w => new WarehouseSave
                {
                    WarehouseId = w.WarehouseId,
                    Type = w.Type,
                    Name = w.Name,
                    Capacity = w.Capacity,
                    PurchasePrice = w.PurchasePrice,
                    MonthlyMaintenance = w.MonthlyMaintenance
                }).ToList(),

                TradingLicenseLevel = player.TradingLicenseLevel,
                TradeVolume = player.TradeVolume,
                BankDeposit = player.BankDeposit,

                Portfolio = new StockPortfolioSave
                {
                    Holdings = new Dictionary<string, int>(player.Portfolio.Holdings),
                    AvgBuyPrice = new Dictionary<string, decimal>(player.Portfolio.AvgBuyPrice),
                    PendingOrders = player.Portfolio.PendingOrders.Select(o => new StockOrderSave
                    {
                        Ticker = o.Ticker,
                        Type = o.Type,
                        TargetPrice = o.TargetPrice,
                        Quantity = o.Quantity,
                        IsBuy = o.IsBuy
                    }).ToList()
                },
                DividendIncome = player.DividendIncome,

                Loans = player.Loans.Select(l => new LoanSave
                {
                    Amount = l.Amount,
                    InterestRate = l.InterestRate,
                    DueDate = l.DueDate,
                    Penalty = l.Penalty,
                    IsDefaulted = l.IsDefaulted
                }).ToList(),

                Factories = player.Factories.Select(f => new IndustrialFactorySave
                {
                    Type = f.Type,
                    Name = f.Name,
                    PurchasePrice = f.PurchasePrice,
                    MonthlyMaintenance = f.MonthlyMaintenance,
                    IsOperational = f.IsOperational,
                    EfficiencyMultiplier = f.EfficiencyMultiplier,
                    ProductionLevel = f.ProductionLevel
                }).ToList()
            };
        }

        private Player RestorePlayerFromSave(PlayerSaveData save)
        {
            var player = new Player { Name = save.Name };
            player.Id = save.Id;
            player.Money = save.Money;
            player.Reputation = save.Reputation;
            player.IntoxicationLevel = save.IntoxicationLevel;
            player.SoberUpTime = save.SoberUpTime;

            player.IsAI = save.IsAI;
            if (save.IsAI && save.Strategy != null)
            {
                player.Strategy = save.Strategy;
                player.DailyProfit = save.DailyProfit;
                player.TotalTrades = save.TotalTrades;
                player.ProfitableTrades = save.ProfitableTrades;
            }

            player.Inventory = save.Inventory.Select(i => new InventoryItem
            {
                ItemName = i.ItemName,
                AveragePrice = i.AveragePrice,
                PurchasePrice = i.AveragePrice,
                Quantity = i.Quantity
            }).ToList();

            if (save.Vehicle != null)
            {
                player.Vehicle = new Vehicle
                {
                    Type = save.Vehicle.Type,
                    Name = save.Vehicle.Name,
                    PurchasePrice = save.Vehicle.PurchasePrice,
                    IsOperational = true
                };

                // Phase 11 Migration: Refresh capacity from constants
                var info = GameEngine.AvailableVehicles.FirstOrDefault(v => v.Type == save.Vehicle.Type);
                player.Vehicle.CargoCapacity = info != default ? info.Capacity : save.Vehicle.CargoCapacity;
            }

            if (save.Property != null)
            {
                player.Property = new Property
                {
                    Type = save.Property.Type,
                    Name = save.Property.Name,
                    PurchasePrice = save.Property.PurchasePrice,
                    MonthlyRent = save.Property.MonthlyRent,
                    GuestCapacity = save.Property.GuestCapacity,
                    BirthdayGiftBonus = save.Property.BirthdayGiftBonus
                };
            }

            if (save.Land != null)
            {
                player.Land = new Land
                {
                    Type = save.Land.Type,
                    Name = save.Land.Name,
                    PurchasePrice = save.Land.PurchasePrice,
                    MaxWarehouseLevel = save.Land.MaxWarehouseLevel
                };
            }

            // Multi-warehouse restore
            player.Warehouses = save.Warehouses.Select(ws => {
                var info = GameEngine.AvailableWarehouses.FirstOrDefault(w => w.Type == ws.Type);
                return new Warehouse
                {
                    WarehouseId = ws.WarehouseId != Guid.Empty ? ws.WarehouseId : Guid.NewGuid(),
                    Type = ws.Type,
                    Name = ws.Name,
                    PurchasePrice = ws.PurchasePrice,
                    MonthlyMaintenance = ws.MonthlyMaintenance,
                    Capacity = info != default ? info.Capacity : ws.Capacity,
                    PurchaseDate = DateTime.Now,
                    LastMaintenancePaid = DateTime.Now
                };
            }).ToList();

            player.TradingLicenseLevel = save.TradingLicenseLevel;
            player.TradeVolume = save.TradeVolume;
            player.BankDeposit = save.BankDeposit;

            // Restore stock portfolio
            if (save.Portfolio != null)
            {
                player.Portfolio.Holdings = new Dictionary<string, int>(save.Portfolio.Holdings);
                player.Portfolio.AvgBuyPrice = new Dictionary<string, decimal>(save.Portfolio.AvgBuyPrice);
                player.Portfolio.PendingOrders = save.Portfolio.PendingOrders.Select(o => new StockOrder
                {
                    Ticker = o.Ticker,
                    Type = o.Type,
                    TargetPrice = o.TargetPrice,
                    Quantity = o.Quantity,
                    IsBuy = o.IsBuy,
                    CreatedAt = DateTime.Now
                }).ToList();
            }
            player.DividendIncome = save.DividendIncome;

            player.Loans = save.Loans.Select(l => new Loan
            {
                Amount = l.Amount,
                InterestRate = l.InterestRate,
                DueDate = l.DueDate,
                Penalty = l.Penalty,
                IsDefaulted = l.IsDefaulted
            }).ToList();

            player.Factories = save.Factories.Select(f => new IndustrialFactory
            {
                Type = f.Type,
                Name = f.Name,
                PurchasePrice = f.PurchasePrice,
                MonthlyMaintenance = f.MonthlyMaintenance,
                IsOperational = f.IsOperational,
                EfficiencyMultiplier = f.EfficiencyMultiplier,
                ProductionLevel = f.ProductionLevel,
                PurchaseDate = DateTime.Now,
                LastMaintenancePaid = DateTime.Now
            }).ToList();

            return player;
        }

        private string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}

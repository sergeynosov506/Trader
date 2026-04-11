using System;
using System.Collections.Concurrent;
using EconomicGame.Configuration;

namespace EconomicGame.Services
{
    public class PlayerService
    {
        private readonly ConcurrentDictionary<Guid, Player> _players = new();
        private Player? _currentPlayer;
        private static readonly Random _random = Random.Shared;

        public Player GetOrCreatePlayer(string name)
        {
            if (_currentPlayer != null)
                return _currentPlayer;

            _currentPlayer = new Player { Name = name };
            _players.TryAdd(_currentPlayer.Id, _currentPlayer);
            return _currentPlayer;
        }

        public Player? GetCurrentPlayer() => _currentPlayer;

        public Player? GetPlayer(Guid id)
        {
            _players.TryGetValue(id, out var player);
            return player;
        }

        public void ResetCurrentPlayer()
        {
            _currentPlayer = null;
        }

        public void SetCurrentPlayer(Player player)
        {
            _currentPlayer = player;
            _players.TryAdd(player.Id, player);
        }

        public IEnumerable<Player> GetAllPlayers() => _players.Values;

        public Player CreateAIPlayer(string name)
        {
            var player = new Player { Name = name, IsAI = true, Money = GameConstants.AIInitialMoney }; // 50k start
            
            // 20% of bots start with assets (vehicle, land, warehouse)
            if (_random.NextDouble() < GameConstants.AIStartWithAssetsChance)
            {
                GiveAIStartingAssets(player);
            }

            _players.TryAdd(player.Id, player);
            return player;
        }

        private void GiveAIStartingAssets(Player ai)
        {
            // Give a basic vehicle
            ai.Vehicle = new Vehicle
            {
                Type = VehicleType.Van,
                Name = "Фургон",
                Emoji = "🚐",
                CargoCapacity = GameConstants.VanCapacity,
                PurchasePrice = GameConstants.VanPrice,
                IsOperational = true,
                PurchaseDate = DateTime.Now
            };

            // Give land
            ai.Land = new Land
            {
                Type = LandType.SmallPlot,
                Name = "Маленький участок",
                Emoji = "🏞️",
                PurchasePrice = GameConstants.SmallPlotPrice,
                PurchaseDate = DateTime.Now,
                MaxWarehouseLevel = GameConstants.SmallPlotMaxWarehouse
            };

            // Give a warehouse
            ai.Warehouses.Add(new Warehouse
            {
                Type = WarehouseType.MiniWarehouse,
                Name = "Мини-склад",
                Emoji = "📦",
                Capacity = GameConstants.MiniWarehouseCapacity,
                PurchasePrice = GameConstants.MiniWarehousePrice,
                MonthlyMaintenance = GameConstants.MiniWarehouseMaintenance,
                PurchaseDate = DateTime.Now,
                LastMaintenancePaid = DateTime.Now
            });

            // Give some starting inventory (raw materials)
            var startItems = new[] { GameConstants.Wheat, GameConstants.Steel, GameConstants.Oil };
            var chosenItem = startItems[_random.Next(startItems.Length)];
            ai.Inventory.Add(new InventoryItem
            {
                ItemName = chosenItem,
                Quantity = _random.Next(50, 200),
                PurchasePrice = 50m,
                AveragePrice = 50m
            });
        }

        public void RegisterPlayer(Player player)
        {
            _players.TryAdd(player.Id, player);
        }

        public void ClearAllPlayers()
        {
            _players.Clear();
            _currentPlayer = null;
        }
    }
}

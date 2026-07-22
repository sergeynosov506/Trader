using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Models;

namespace EconomicGame.Services
{
    public class CorporateActionService
    {
        private readonly PlayerService _playerService;

        public CorporateActionService(PlayerService playerService)
        {
            _playerService = playerService;
        }

        public decimal CalculateSabotageCost(Player target)
        {
            // Base $10k + 5% of rival's current capital
            return 10000m + (target.Money * 0.05m);
        }

        public string SabotageRival(Guid targetId, DateTime currentTime, Guid? actorId = null)
        {
            var actor = actorId.HasValue 
                ? _playerService.GetAllPlayers().FirstOrDefault(p => p.Id == actorId.Value)
                : _playerService.GetCurrentPlayer();
            if (actor == null) return "Игрок не найден.";

            var target = _playerService.GetAllPlayers().FirstOrDefault(p => p.Id == targetId);
            if (target == null) return "Цель не найдена.";

            if (target.TradingLicenseLevel > 0)
            {
                return $"{target.Name} обладает торговой лицензией уровня {target.TradingLicenseLevel}, что делает их компанию полностью защищенной от саботажа и блокировок!";
            }

            decimal cost = CalculateSabotageCost(target);

            if (actor.Money < cost) return $"Недостаточно средств. Стоимость саботажа {target.Name} составляет {cost:C}.";

            actor.Money -= cost;
            target.IsSabotaged = true;
            target.SabotageEndTime = currentTime.AddHours(12); // Sabotaged for 12 game hours
            
            return $"Успех! {target.Name} временно выведен из строя. Торги остановлены. (Списано {cost:C})";
        }

        public string GetMarketIntel(Guid targetId)
        {
            var player = _playerService.GetCurrentPlayer();
            if (player == null) return "Игрок не найден.";

            const decimal intelCost = 5000m;
            if (player.Money < intelCost) return "Денег на разведку нет. Нужно $5,000.";

            var target = _playerService.GetAllPlayers().FirstOrDefault(p => p.Id == targetId);
            if (target == null) return "Цель не найдена.";

            player.Money -= intelCost;
            
            var topItems = target.Inventory
                .OrderByDescending(i => i.Quantity)
                .Take(3)
                .Select(i => $"{i.ItemName} ({i.Quantity} ед.)");

            return $"Разведка докладывает: Склад {target.Name} забит товарами. Крупнейшие позиции: {string.Join(", ", topItems)}.";
        }

        public string HostileTakeover(Guid targetId)
        {
            var player = _playerService.GetCurrentPlayer();
            if (player == null) return "Игрок не найден.";

            var target = _playerService.GetAllPlayers().FirstOrDefault(p => p.Id == targetId);
            if (target == null) return "Цель не найдена.";
            if (target.OwnerId != null) return "Этот узел уже принадлежит другой корпорации.";

            // Takeover cost: 5x target's current money, minimum $1.5M
            decimal cost = Math.Max(target.Money * 5, 1500000m);

            if (player.Money < cost) return $"Недостаточно средств. Для поглощения {target.Name} требуется {cost:C}.";

            player.Money -= cost;
            player.OwnedAIIds.Add(target.Id);
            target.OwnerId = player.Id;
            
            return $"Поздравляем! {target.Name} теперь является частью вашей империи. Вы будете получать 30% от их прибыли.";
        }

        public void ProcessSubsidaryPayouts()
        {
            var player = _playerService.GetCurrentPlayer();
            if (player == null || !player.OwnedAIIds.Any()) return;

            foreach (var aiId in player.OwnedAIIds)
            {
                var ai = _playerService.GetAllPlayers().FirstOrDefault(p => p.Id == aiId);
                if (ai != null && ai.DailyProfit > 0)
                {
                    decimal payout = ai.DailyProfit * 0.3m;
                    ai.Money -= payout;
                    player.Money += payout;
                    // Note: Reset daily profit on AI is handled by game loop, but we take a cut here
                }
            }
        }
    }
}

using System;
using EconomicGame.Models;

namespace EconomicGame.Services
{
    public class InsuranceService
    {
        private readonly PlayerService _playerService;

        public InsuranceService(PlayerService playerService)
        {
            _playerService = playerService;
        }

        public string BuyInsurance(DateTime currentTime)
        {
            var player = _playerService.GetCurrentPlayer();
            if (player == null) return "Игрок не найден.";

            const decimal insuranceCost = 25000m; // Monthly fee
            if (player.Money < insuranceCost) return $"Недостаточно средств. Полис стоит {insuranceCost:C}.";

            player.Money -= insuranceCost;
            player.HasInsurance = true;
            
            // Insurance lasts for 30 game days
            player.InsuranceExpiry = currentTime.AddDays(30);

            return $"Полис оформлен! Вы застрахованы до {player.InsuranceExpiry.Value:dd.MM HH:mm}. (Списано {insuranceCost:C})";
        }

        public (bool IsCovered, string Message) ProcessClaim(Player player, decimal lostValue)
        {
            if (!player.HasInsurance || !player.InsuranceExpiry.HasValue)
                return (false, "У вас нет активной страховки.");

            // Insurance covers 80% of losses
            decimal reimbursement = lostValue * 0.8m;
            player.Money += reimbursement;

            return (true, $"🛡️ Страховка сработала! Вы получили компенсацию в размере {reimbursement:C} (80% от убытков).");
        }

        public void UpdateInsuranceStatus(DateTime currentTime)
        {
            var player = _playerService.GetCurrentPlayer();
            if (player != null && player.HasInsurance && player.InsuranceExpiry.HasValue)
            {
                if (currentTime >= player.InsuranceExpiry.Value)
                {
                    player.HasInsurance = false;
                    player.InsuranceExpiry = null;
                    // Note: In a real app we might send a notification here
                }
            }
        }
    }
}

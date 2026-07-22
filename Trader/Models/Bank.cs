using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Configuration;

namespace EconomicGame
{
    public class Bank
    {
        private static readonly Random _random = Random.Shared;
        public decimal CurrentInterestRate { get; private set; } = GameConstants.DefaultInterestRate;

        public void UpdateInterestRate()
        {
            // Fluctuate between 2% and 10%
            var change = (decimal)(_random.NextDouble() * 0.02 - 0.01); // +/- 1%
            CurrentInterestRate += change;
            if (CurrentInterestRate < 0.02m) CurrentInterestRate = 0.02m;
            if (CurrentInterestRate > 0.10m) CurrentInterestRate = 0.10m;
        }

        /// <summary>
        /// Get personalized interest rate based on player reputation.
        /// Higher reputation = lower interest rate.
        /// </summary>
        public decimal GetInterestRateForPlayer(Player player)
        {
            // Reputation 0-100, max discount at 100
            var discountFactor = player.Reputation / 100m;
            var discount = discountFactor * GameConstants.MaxReputationDiscount;
            return Math.Max(0.02m, CurrentInterestRate - discount);
        }

        /// <summary>
        /// Get deposit interest rate (typically lower than loan rate)
        /// </summary>
        public decimal GetDepositInterestRate()
        {
            return CurrentInterestRate * GameConstants.DepositInterestMultiplier;
        }

        /// <summary>
        /// Make a deposit into savings account
        /// </summary>
        public string MakeDeposit(Player player, decimal amount)
        {
            if (amount <= 0)
                return "❌ Сумма вклада должна быть больше 0";
            
            if (player.Money < amount)
                return $"❌ Недостаточно денег! У вас {player.Money:C}";
            
            player.Money -= amount;
            player.BankDeposit += amount;
            player.MonthlyExpenses += amount; // Track as "expense" (transfer out of wallet)
            
            if (player.LastInterestPaid == null)
                player.LastInterestPaid = DateTime.Now;
            
            return $"✅ Внесено {amount:C} на депозит. Всего на счету: {player.BankDeposit:C}";
        }

        /// <summary>
        /// Withdraw from savings account
        /// </summary>
        public string Withdraw(Player player, decimal amount)
        {
            if (amount <= 0)
                return "❌ Сумма снятия должна быть больше 0";
            
            if (player.BankDeposit < amount)
                return $"❌ На счету только {player.BankDeposit:C}";
            
            player.BankDeposit -= amount;
            player.Money += amount;
            player.MonthlyIncome += amount; // Track as "income" (transfer into wallet)
            
            return $"✅ Снято {amount:C} со счёта. Осталось на депозите: {player.BankDeposit:C}";
        }

        /// <summary>
        /// Pay interest on deposits (called daily or periodically)
        /// </summary>
        public decimal PayDepositInterest(Player player, DateTime currentTime)
        {
            if (player.BankDeposit <= 0) return 0;
            
            // Pay interest every 7 game days
            if (player.LastInterestPaid == null)
            {
                player.LastInterestPaid = currentTime;
                return 0;
            }
            
            var daysSinceLastPayment = (currentTime - player.LastInterestPaid.Value).Days;
            if (daysSinceLastPayment < GameConstants.DepositInterestPaymentDays) return 0;

            // Weekly interest (annual rate / 52 weeks)
            var weeklyRate = GetDepositInterestRate() / 52m;
            var weeks = daysSinceLastPayment / 7;
            var interest = player.BankDeposit * weeklyRate * weeks;

            player.BankDeposit += interest;
            player.MonthlyIncome += interest;
            // Advance exactly by the whole weeks paid out so fractional days carry forward
            player.LastInterestPaid = player.LastInterestPaid.Value.AddDays(weeks * 7);

            return interest;
        }

        public void TakeLoan(Player player, decimal amount, int months, DateTime currentTime)
        {
            var loan = new Loan
            {
                Amount = amount,
                InterestRate = GetInterestRateForPlayer(player), // Use reputation-based rate
                DueDate = currentTime.AddDays(months * 30), // treating months as 30 days
                Penalty = 0,
                IsDefaulted = false
            };
            player.Loans.Add(loan);
            player.Money += amount;
        }

        public void CheckLoans(Player player, DateTime currentTime)
        {
            foreach (var loan in player.Loans.ToList()) // ToList to allow modification/removal
            {
                if (currentTime > loan.DueDate)
                {
                    if (!loan.IsDefaulted)
                    {
                        // First time default
                        loan.IsDefaulted = true;
                        loan.Penalty += loan.Amount * 0.10m; // 10% immediate penalty
                    }
                    
                    // Check if grace period (e.g. 7 days) is over
                    if (currentTime > loan.DueDate.AddDays(7))
                    {
                        SeizeAssets(player, loan);
                    }
                }
            }
        }

        private void SeizeAssets(Player player, Loan loan)
        {
            var totalDebt = loan.Amount + loan.Penalty;
            
            // Step 1: Cash
            if (player.Money >= totalDebt)
            {
                player.Money -= totalDebt;
                player.Loans.Remove(loan);
                return;
            }
            
            totalDebt -= player.Money;
            player.Money = 0;

            // Step 2: Inventory (50% value)
            if (player.Inventory.Count > 0)
            {
                foreach (var item in player.Inventory.ToList())
                {
                    var value = item.PurchasePrice * item.Quantity * 0.5m;
                    if (value >= totalDebt)
                    {
                        // Sell partial
                        var quantityNeeded = (int)Math.Ceiling(totalDebt / (item.PurchasePrice * 0.5m));
                        if (quantityNeeded > item.Quantity) quantityNeeded = item.Quantity; // Should not happen based on if

                        item.Quantity -= quantityNeeded;
                        if (item.Quantity == 0) player.Inventory.Remove(item);
                        
                        player.Loans.Remove(loan);
                        return; 
                    }
                    else
                    {
                        totalDebt -= value;
                        player.Inventory.Remove(item);
                    }
                }
            }

            // Step 3: Vehicles (50% of purchase price)
            foreach (var vehicle in player.Vehicles.ToList())
            {
                var vehicleValue = vehicle.PurchasePrice * 0.5m;
                if (vehicleValue >= totalDebt)
                {
                    player.Vehicles.Remove(vehicle);
                    player.Loans.Remove(loan);
                    return;
                }
                else
                {
                    totalDebt -= vehicleValue;
                    player.Vehicles.Remove(vehicle);
                }
            }

            // Step 4: Warehouses (50% of purchase price)
            foreach (var warehouse in player.Warehouses.ToList())
            {
                // Only seize empty warehouses to avoid losing user's inventory invisibly
                var remainingCapacity = player.Vehicles.Sum(v => v.CargoCapacity)
                    + player.Warehouses.Where(w => w.WarehouseId != warehouse.WarehouseId).Sum(w => w.Capacity);
                if (player.Inventory.Sum(i => i.Quantity) > remainingCapacity) break;

                var value = warehouse.PurchasePrice * 0.5m;
                if (value >= totalDebt)
                {
                    player.Warehouses.Remove(warehouse);
                    player.Loans.Remove(loan);
                    return;
                }
                totalDebt -= value;
                player.Warehouses.Remove(warehouse);
            }

            // Step 5: Bankruptcy — still some debt, mark and drop loan
            player.IsBankrupt = true;
            player.Loans.Remove(loan);
        }
    }
}
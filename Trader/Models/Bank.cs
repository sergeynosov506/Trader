using System;
using System.Collections.Generic;
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
            player.LastInterestPaid = currentTime;
            
            return interest;
        }

        public void TakeLoan(Player player, decimal amount, int months)
        {
            var loan = new Loan
            {
                Amount = amount,
                InterestRate = CurrentInterestRate,
                DueDate = DateTime.Now.AddMonths(months) // Ideally use GameTime, but Loan model stores DateTime. 
                // Wait, if GameEngine.CurrentTime is used for checking, DueDate should be relative to that?
                // For simplicity in this iteration, assuming Date mapping is consistent.
                // Or better: We should pass CurrentTime to TakeLoan.
                // Let's assume for now 1 game day = 1 real day in DateTime logic if we are using CurrentTime ticks.
                // Actually, GameEngine adds 15 mins. So days pass fast. "Months" in UI should probably mean "Game Days" or "Game Weeks"?
                // Let's stick to DateTime.Now for creation if checking against DateTime.Now, BUT plan said CheckLoans uses CurrentTime.
                // So checking logic needs to match creation logic. 
                // FIX: Update DueDate to be relative to GameEngine.CurrentTime passed in.
            };
            // Since I can't easily change signature in UI without breaking it, I'll stick to DateTime logic 
            // BUT GameEngine.CurrentTime is what drives the game.
            // I will update TakeLoan signature later or now? 
            // Let's rely on CheckLoans passing the reference time.
            
            // Revert: I will use the passed 'currentTime' in CheckLoans. 
            // But TakeLoan needs to know "Now".
            // Adding 'DateTime currentTime' to TakeLoan.
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

            // Step 3: Vehicle (50% value)
            if (player.Vehicle != null)
            {
                var vehicleValue = 1000m; // Basic Car cost 2000, sell 1000
                if (vehicleValue >= totalDebt)
                {
                    player.Vehicle = null;
                    player.Loans.Remove(loan);
                    return;
                }
                else
                {
                    totalDebt -= vehicleValue;
                    player.Vehicle = null;
                }
            }

            // Step 4: Bankruptcy
            player.IsBankrupt = true;
            // Debt remains mostly unpaid...
            player.Loans.Remove(loan); // Clear this specific loan loop? 
            // Or keep it? Simpler to just clear it and mark bankrupt.
        }
    }
}
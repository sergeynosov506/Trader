using System;
using System.Collections.Generic;
using EconomicGame.Configuration;

namespace EconomicGame
{
    public class StockExchange
    {
        private static readonly Random _random = Random.Shared;
        public List<MarketItem> Items { get; set; } = new List<MarketItem>();
        public List<TradeListing> Listings { get; set; } = new List<TradeListing>();

        public StockExchange(List<MarketItem> items)
        {
            Items = items;
        }

        public void UpdatePrices()
        {
            foreach (var item in Items)
            {
                // --- Phase 5: Inflation Cooling ---
                // If a price becomes "Hyper-inflated" (> $1000), it feels natural downward pressure
                // This prevents the economy from becoming purely about astronomical numbers
                if (item.CurrentPrice > GameConstants.PriceHyperThreshold)
                {
                    item.CurrentPrice *= GameConstants.InflationCoolingFactor;
                }

                // --- Phase 10: Foundation Cooling ---
                // Raw materials (Wheat, Steel, etc.) should stay affordable. 
                // If price > $200, apply additional cooling.
                if (GameConstants.RawMaterials.Contains(item.Name) && item.CurrentPrice > 200m)
                {
                    item.CurrentPrice *= GameConstants.InflationCoolingFactor;
                }

                // --- Revive Dead Prices ---
                if (item.CurrentPrice < GameConstants.MinPrice)
                    item.CurrentPrice = GameConstants.MinPrice * 10; // Jump-start back to a reasonable base

                // Phase 2: AI-Driven Supply/Demand
                // AI Buying increases demand (price up), AI Selling increases supply (price down)
                decimal netVolume = item.BuyVolume - item.SellVolume;
                
                // Scale impact: Use divisor to control market depth (e.g., 1% shift per X units)
                decimal marketImpact = (netVolume / GameConstants.MarketImpactDivisor) * GameConstants.DemandSupplyImpact;

                // --- Scarcity Impact ---
                // If stock is below the threshold, apply upward pressure proportional to stock emptiness
                // This ensures price rises when items are "sold out" even if no trades are happening
                if (item.AvailableQuantity < GameConstants.ScarcityThreshold)
                {
                    decimal scarcityFactor = (GameConstants.ScarcityThreshold - item.AvailableQuantity) / (decimal)GameConstants.ScarcityThreshold;
                    marketImpact += scarcityFactor * GameConstants.ScarcityImpact;
                }

                // Baseline noise
                var randomness = (decimal)(_random.NextDouble() * (double)GameConstants.PriceRandomnessRange - (double)GameConstants.PriceRandomnessRange / 2);
                
                // --- Volatility Guard ---
                // Cap the maximum possible change in a single tick to prevent flash crashes
                decimal totalDelta = marketImpact + randomness;
                totalDelta = Math.Clamp(totalDelta, -GameConstants.MaxPriceChangePerTick, GameConstants.MaxPriceChangePerTick);

                // Update Price
                item.CurrentPrice *= (1 + totalDelta);

                // --- Price Floor ---
                if (item.CurrentPrice < GameConstants.MinPrice)
                    item.CurrentPrice = GameConstants.MinPrice;

                item.PriceHistory.Add(item.CurrentPrice);
                
                // Reset volumes for next tick
                item.BuyVolume = 0;
                item.SellVolume = 0;
                
                if (item.PriceHistory.Count > GameConstants.MaxPriceHistoryDays) 
                    item.PriceHistory.RemoveAt(0);

                // --- Stock Replenishment (BUFFED for 100-node population) ---
                // Raw materials regenerate faster now to prevent perpetual zero-stock
                if (GameConstants.RawMaterials.Contains(item.Name) && item.AvailableQuantity < 10000)
                {
                    item.AvailableQuantity += _random.Next(50, 150);
                }
                else if (GameConstants.Products.Contains(item.Name) && item.AvailableQuantity < 1000)
                {
                    // Finished goods also replenish very slowly (simulating outside market imports)
                    // This prevents items from being permanently unavailable if production stops
                    item.AvailableQuantity += _random.Next(1, 5);
                }

                // --- Neural Quantitative Easing (Price Support) ---
                // If the price is more than 50% below the 30-day average, we apply a subtle upward nudge
                // This helps the player (and the AI) recover from severe market crashes
                if (item.PriceHistory.Count >= 10)
                {
                    decimal avgPrice = item.PriceHistory.Average();
                    if (item.CurrentPrice < avgPrice * 0.5m)
                    {
                        item.CurrentPrice *= 1.05m; // 5% support boost per tick
                    }
                }
            }
        }
    }
}
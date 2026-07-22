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

                bool isProduct = GameConstants.Products.Contains(item.Name);

                // Phase 2: AI-Driven Supply/Demand
                // AI Buying increases demand (price up), AI Selling increases supply (price down)
                decimal netVolume = item.BuyVolume - item.SellVolume;

                // Scale impact: Use divisor to control market depth (e.g., 1% shift per X units).
                // Products react much stronger to volume than raw materials (rebalance):
                // dumping finished goods must actually crash their price.
                decimal impactDivisor = isProduct ? GameConstants.MarketImpactDivisorProducts : GameConstants.MarketImpactDivisor;
                decimal marketImpact = (netVolume / impactDivisor) * GameConstants.DemandSupplyImpact;

                // --- Demand Capacity (rebalance) ---
                // The market absorbs only so much product per unit of time at the current price.
                // Rolling sell-pressure decays each tick; flooding beyond capacity adds an extra
                // downward push, so monoculture dumping strangles itself.
                item.AccumulatedSellPressure = item.AccumulatedSellPressure * GameConstants.DemandPressureDecay + item.SellVolume;
                if (isProduct && item.AccumulatedSellPressure > GameConstants.ProductDemandCapacity)
                {
                    decimal overflow = item.AccumulatedSellPressure - GameConstants.ProductDemandCapacity;
                    decimal dumpPenalty = Math.Min(GameConstants.DemandOverflowMaxImpact, overflow / GameConstants.DemandOverflowImpactDivisor);
                    marketImpact -= dumpPenalty;
                }

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
                // RAW MATERIALS ONLY (rebalance): if the price is more than 50% below the average,
                // apply a subtle upward nudge. Finished products get no bailout — if you flood
                // the market with sugar, you live with the crash you created.
                if (!isProduct && item.PriceHistory.Count >= 10)
                {
                    decimal avgPrice = item.PriceHistory.Average();
                    if (item.CurrentPrice < avgPrice * 0.5m)
                    {
                        item.CurrentPrice *= 1.05m; // 5% support boost per tick
                    }
                }

                // --- Product Value Floor ---
                // Finished products shouldn't be "free". If price < $10 and it's a product, 
                // apply pressure to bring it back to a base commercial value.
                if (GameConstants.Products.Contains(item.Name) && item.CurrentPrice < 20m)
                {
                    item.CurrentPrice += 2.0m; // Flat $2 increase per tick to jump-start from near-zero
                }
            }
        }
    }
}
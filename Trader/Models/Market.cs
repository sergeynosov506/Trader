using System;
using System.Collections.Generic;

namespace EconomicGame
{
    public class MarketItem
    {
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
        public decimal CurrentPrice { get; set; }
        public List<decimal> PriceHistory { get; set; } = new List<decimal>();
        public int AvailableQuantity { get; set; }

        // AI Market Impact Tracking
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }

        /// <summary>
        /// Rolling sell-pressure (decays every tick). When it exceeds the market's
        /// demand capacity for products, dumping is punished with extra price drops.
        /// </summary>
        public decimal AccumulatedSellPressure { get; set; }
    }

    public class Market
    {
        public List<MarketItem> Items { get; set; } = new List<MarketItem>();

        public void BuyItem(Player player, MarketItem item, int quantity)
        {
            var totalCost = item.CurrentPrice * quantity;
            if (player.Money >= totalCost && item.AvailableQuantity >= quantity)
            {
                player.Money -= totalCost;
                item.AvailableQuantity -= quantity;
                player.Inventory.Add(new InventoryItem { ItemName = item.Name, PurchasePrice = item.CurrentPrice, Quantity = quantity });
            }
        }

        public void SellItem(Player player, InventoryItem item, int quantity)
        {
            var marketItem = Items.Find(i => i.Name == item.ItemName);
            if (marketItem != null && item.Quantity >= quantity)
            {
                player.Money += marketItem.CurrentPrice * quantity;
                marketItem.AvailableQuantity += quantity;
                item.Quantity -= quantity;
                if (item.Quantity == 0) player.Inventory.Remove(item);
            }
        }
    }
}
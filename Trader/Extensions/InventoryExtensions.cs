using System.Collections.Generic;
using System.Linq;

namespace EconomicGame.Extensions
{
    public static class InventoryExtensions
    {
        /// <summary>
        /// Adds an item to inventory or updates existing item with weighted average price.
        /// </summary>
        public static void AddOrUpdateItem(this List<InventoryItem> inventory, 
            string itemName, decimal price, int quantity)
        {
            var existing = inventory.FirstOrDefault(i => i.ItemName == itemName);
            if (existing != null)
            {
                // Calculate weighted average price
                existing.AveragePrice = 
                    (existing.AveragePrice * existing.Quantity + price * quantity) 
                    / (existing.Quantity + quantity);
                existing.Quantity += quantity;
            }
            else
            {
                inventory.Add(new InventoryItem 
                { 
                    ItemName = itemName, 
                    PurchasePrice = price,
                    AveragePrice = price,
                    Quantity = quantity 
                });
            }
        }

        /// <summary>
        /// Removes quantity from inventory item. Returns true if successful.
        /// </summary>
        public static bool RemoveQuantity(this List<InventoryItem> inventory, 
            string itemName, int quantity)
        {
            var existing = inventory.FirstOrDefault(i => i.ItemName == itemName);
            if (existing == null || existing.Quantity < quantity)
                return false;

            existing.Quantity -= quantity;
            if (existing.Quantity == 0)
                inventory.Remove(existing);
            
            return true;
        }
    }
}

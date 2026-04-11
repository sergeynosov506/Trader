using System;

namespace EconomicGame
{
    public class TradeListing
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SellerId { get; set; }
        public required string SellerName { get; set; }
        public required string ItemName { get; set; }
        public int Quantity { get; set; }
        public decimal PricePerUnit { get; set; }
        public DateTime ListedDate { get; set; } = DateTime.Now;
        public bool AutoRepeat { get; set; }

        public decimal TotalPrice => PricePerUnit * Quantity;
    }
}

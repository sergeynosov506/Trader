using System;
using System.Collections.Generic;
using EconomicGame.Configuration;

namespace EconomicGame.Models
{
    /// <summary>
    /// Represents a publicly traded stock on the stock exchange.
    /// Each stock is linked to a commodity — its price partially correlates.
    /// </summary>
    public class Stock
    {
        public Guid StockId { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Ticker symbol, e.g. "WHEAT-CO", "STEEL-INC"
        /// </summary>
        public required string Ticker { get; set; }
        
        /// <summary>
        /// Company name displayed in UI
        /// </summary>
        public required string CompanyName { get; set; }
        
        /// <summary>
        /// Emoji for visual flair
        /// </summary>
        public string Emoji { get; set; } = "📊";
        
        /// <summary>
        /// Current share price
        /// </summary>
        public decimal SharePrice { get; set; }
        
        /// <summary>
        /// Annual dividend yield (as decimal, e.g. 0.03 = 3%)
        /// </summary>
        public decimal DividendYield { get; set; }
        
        /// <summary>
        /// Total shares outstanding
        /// </summary>
        public int TotalShares { get; set; }
        
        /// <summary>
        /// Shares available for purchase on the market
        /// </summary>
        public int AvailableShares { get; set; }
        
        /// <summary>
        /// Historical share prices for charting
        /// </summary>
        public List<decimal> PriceHistory { get; set; } = new List<decimal>();
        
        /// <summary>
        /// Market capitalization
        /// </summary>
        public decimal MarketCap => SharePrice * TotalShares;
        
        /// <summary>
        /// The commodity this stock is linked to (price correlation)
        /// </summary>
        public required string LinkedCommodity { get; set; }
        
        /// <summary>
        /// How strongly the stock correlates to its commodity (0.0-1.0)
        /// </summary>
        public decimal CorrelationFactor { get; set; } = GameConstants.StockPriceCorrelation;
        
        /// <summary>
        /// Trading volume tracking for AI market impact
        /// </summary>
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }
    }

    /// <summary>
    /// Static list of all stocks available on the stock market.
    /// Initialized once; prices update dynamically.
    /// </summary>
    public static class StockDefinitions
    {
        public static List<Stock> CreateInitialStocks() => new List<Stock>
        {
            new Stock
            {
                Ticker = "AGRI",
                CompanyName = "АгроХолдинг",
                Emoji = "🌾",
                SharePrice = 25m,
                DividendYield = 0.04m,
                TotalShares = GameConstants.InitialSharesPerStock,
                AvailableShares = GameConstants.InitialSharesPerStock,
                LinkedCommodity = GameConstants.Wheat
            },
            new Stock
            {
                Ticker = "STLX",
                CompanyName = "СтальЭкспорт",
                Emoji = "🔩",
                SharePrice = 50m,
                DividendYield = 0.03m,
                TotalShares = GameConstants.InitialSharesPerStock,
                AvailableShares = GameConstants.InitialSharesPerStock,
                LinkedCommodity = GameConstants.Steel
            },
            new Stock
            {
                Ticker = "OILP",
                CompanyName = "НефтеПром",
                Emoji = "🛢️",
                SharePrice = 75m,
                DividendYield = 0.05m,
                TotalShares = GameConstants.InitialSharesPerStock,
                AvailableShares = GameConstants.InitialSharesPerStock,
                LinkedCommodity = GameConstants.Oil
            },
            new Stock
            {
                Ticker = "GOLD",
                CompanyName = "ЗолотоДобыча",
                Emoji = "🥇",
                SharePrice = 180m,
                DividendYield = 0.02m,
                TotalShares = 5000,
                AvailableShares = 5000,
                LinkedCommodity = GameConstants.Gold
            },
            new Stock
            {
                Ticker = "CUPR",
                CompanyName = "МедьИнвест",
                Emoji = "🔧",
                SharePrice = 35m,
                DividendYield = 0.035m,
                TotalShares = GameConstants.InitialSharesPerStock,
                AvailableShares = GameConstants.InitialSharesPerStock,
                LinkedCommodity = GameConstants.Copper
            },
            new Stock
            {
                Ticker = "TECH",
                CompanyName = "ТехноГрупп",
                Emoji = "💻",
                SharePrice = 120m,
                DividendYield = 0.015m,
                TotalShares = 8000,
                AvailableShares = 8000,
                LinkedCommodity = GameConstants.Electronics
            },
            new Stock
            {
                Ticker = "PHRM",
                CompanyName = "ФармаЛаб",
                Emoji = "💊",
                SharePrice = 200m,
                DividendYield = 0.06m,
                TotalShares = 5000,
                AvailableShares = 5000,
                LinkedCommodity = GameConstants.Pharmaceuticals
            },
            new Stock
            {
                Ticker = "LUXE",
                CompanyName = "ЛюксБренд",
                Emoji = "💎",
                SharePrice = 300m,
                DividendYield = 0.025m,
                TotalShares = 3000,
                AvailableShares = 3000,
                LinkedCommodity = GameConstants.LuxuryGoods
            },
            new Stock
            {
                Ticker = "CHEM",
                CompanyName = "ХимСинтез",
                Emoji = "🧪",
                SharePrice = 65m,
                DividendYield = 0.04m,
                TotalShares = GameConstants.InitialSharesPerStock,
                AvailableShares = GameConstants.InitialSharesPerStock,
                LinkedCommodity = GameConstants.Chemicals
            },
            new Stock
            {
                Ticker = "TXTL",
                CompanyName = "ТекстильПро",
                Emoji = "🧵",
                SharePrice = 20m,
                DividendYield = 0.05m,
                TotalShares = GameConstants.InitialSharesPerStock,
                AvailableShares = GameConstants.InitialSharesPerStock,
                LinkedCommodity = GameConstants.Textiles
            }
        };
    }
}

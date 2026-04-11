using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Configuration;
using EconomicGame.Models;

namespace EconomicGame.Services
{
    /// <summary>
    /// Manages the stock market: stock prices, orders, dividends, and AI stock trading.
    /// </summary>
    public class StockMarketService
    {
        private static readonly Random _random = Random.Shared;
        private readonly PlayerService _playerService;
        
        public List<Stock> Stocks { get; private set; }
        public DateTime LastDividendPaid { get; set; } = DateTime.MinValue;

        public StockMarketService(PlayerService playerService)
        {
            _playerService = playerService;
            Stocks = StockDefinitions.CreateInitialStocks();
        }

        /// <summary>
        /// Update all stock prices based on linked commodity prices + noise.
        /// Called each game tick.
        /// </summary>
        public void UpdateStockPrices(List<MarketItem> commodityPrices)
        {
            foreach (var stock in Stocks)
            {
                var commodity = commodityPrices.FirstOrDefault(c => c.Name == stock.LinkedCommodity);
                if (commodity == null) continue;

                // Price correlation with commodity
                decimal commodityChangePercent = 0;
                if (commodity.PriceHistory.Count >= 2)
                {
                    var prev = commodity.PriceHistory[commodity.PriceHistory.Count - 2];
                    var curr = commodity.CurrentPrice;
                    if (prev > 0) commodityChangePercent = (curr - prev) / prev;
                }

                // Stock price = correlated commodity move + random noise + volume impact
                decimal correlatedChange = commodityChangePercent * stock.CorrelationFactor;
                decimal noise = (decimal)(_random.NextDouble() * (double)GameConstants.StockPriceNoise * 2 - (double)GameConstants.StockPriceNoise);
                
                // Volume impact
                decimal netVolume = stock.BuyVolume - stock.SellVolume;
                decimal volumeImpact = netVolume / (stock.TotalShares * 0.01m); // 1% of total shares = 1% price move

                decimal totalChange = correlatedChange + noise + volumeImpact * 0.01m;
                totalChange = Math.Clamp(totalChange, -0.10m, 0.10m); // Max 10% move per tick

                stock.SharePrice *= (1 + totalChange);
                stock.SharePrice = Math.Max(0.10m, stock.SharePrice); // Floor

                // Record history
                stock.PriceHistory.Add(stock.SharePrice);
                if (stock.PriceHistory.Count > GameConstants.MaxStockPriceHistory)
                    stock.PriceHistory.RemoveAt(0);

                // Reset volumes
                stock.BuyVolume = 0;
                stock.SellVolume = 0;

                // Replenish available shares slowly
                if (stock.AvailableShares < stock.TotalShares * 0.8)
                {
                    stock.AvailableShares += _random.Next(10, 50);
                    stock.AvailableShares = Math.Min(stock.AvailableShares, stock.TotalShares);
                }
            }
        }

        /// <summary>
        /// Buy shares of a stock
        /// </summary>
        public string BuyStock(Player player, string ticker, int quantity)
        {
            if (player == null) return "Игрок не найден!";
            
            var stock = Stocks.FirstOrDefault(s => s.Ticker == ticker);
            if (stock == null) return "Акция не найдена!";

            if (stock.AvailableShares < quantity)
                return $"Недостаточно акций! Доступно: {stock.AvailableShares}";

            decimal totalCost = stock.SharePrice * quantity;
            decimal commission = totalCost * GameConstants.StockBrokerFee;
            decimal totalWithFee = totalCost + commission;

            if (player.Money < totalWithFee)
                return $"Не хватает денег! Нужно {totalWithFee:C} (включая комиссию {commission:C})";

            // Execute purchase
            player.Money -= totalWithFee;
            stock.AvailableShares -= quantity;
            stock.BuyVolume += quantity;

            // Update portfolio
            if (player.Portfolio.Holdings.ContainsKey(ticker))
            {
                var currentShares = player.Portfolio.Holdings[ticker];
                var currentAvg = player.Portfolio.AvgBuyPrice[ticker];
                
                // Recalculate average price
                var newAvg = ((currentAvg * currentShares) + (stock.SharePrice * quantity)) / (currentShares + quantity);
                player.Portfolio.Holdings[ticker] += quantity;
                player.Portfolio.AvgBuyPrice[ticker] = newAvg;
            }
            else
            {
                player.Portfolio.Holdings[ticker] = quantity;
                player.Portfolio.AvgBuyPrice[ticker] = stock.SharePrice;
            }

            player.TradeVolume += totalCost;
            return $"Куплено {quantity} акций {stock.CompanyName} ({ticker}) по {stock.SharePrice:C} = {totalCost:C} (+{commission:C} комиссия)";
        }

        /// <summary>
        /// Sell shares of a stock
        /// </summary>
        public string SellStock(Player player, string ticker, int quantity)
        {
            if (player == null) return "Игрок не найден!";

            var stock = Stocks.FirstOrDefault(s => s.Ticker == ticker);
            if (stock == null) return "Акция не найдена!";

            if (!player.Portfolio.Holdings.ContainsKey(ticker) || player.Portfolio.Holdings[ticker] < quantity)
                return $"У вас нет столько акций {ticker}!";

            decimal totalRevenue = stock.SharePrice * quantity;
            decimal commission = totalRevenue * GameConstants.StockBrokerFee;
            decimal netRevenue = totalRevenue - commission;

            // Execute sale
            player.Money += netRevenue;
            stock.AvailableShares += quantity;
            stock.SellVolume += quantity;

            player.Portfolio.Holdings[ticker] -= quantity;
            if (player.Portfolio.Holdings[ticker] <= 0)
            {
                player.Portfolio.Holdings.Remove(ticker);
                player.Portfolio.AvgBuyPrice.Remove(ticker);
            }

            player.TradeVolume += totalRevenue;

            var avgBuy = player.Portfolio.AvgBuyPrice.GetValueOrDefault(ticker, stock.SharePrice);
            var profit = (stock.SharePrice - avgBuy) * quantity;

            return $"Продано {quantity} акций {stock.CompanyName} ({ticker}) по {stock.SharePrice:C} = {netRevenue:C} (после комиссии). P&L: {profit:+#,##0.00;-#,##0.00}";
        }

        /// <summary>
        /// Place a limit or stop-loss order
        /// </summary>
        public string PlaceOrder(Player player, string ticker, OrderType orderType, decimal targetPrice, int quantity, bool isBuy)
        {
            if (player == null) return "Игрок не найден!";

            var stock = Stocks.FirstOrDefault(s => s.Ticker == ticker);
            if (stock == null) return "Акция не найдена!";

            if (!isBuy && (!player.Portfolio.Holdings.ContainsKey(ticker) || player.Portfolio.Holdings[ticker] < quantity))
                return $"У вас нет столько акций {ticker} для продажи!";

            var order = new StockOrder
            {
                Ticker = ticker,
                Type = orderType,
                TargetPrice = targetPrice,
                Quantity = quantity,
                IsBuy = isBuy,
                CreatedAt = DateTime.Now
            };

            player.Portfolio.PendingOrders.Add(order);

            var typeStr = orderType switch
            {
                OrderType.Limit => "Лимитный",
                OrderType.StopLoss => "Стоп-лосс",
                _ => "Рыночный"
            };

            return $"{typeStr} ордер создан: {(isBuy ? "Купить" : "Продать")} {quantity} {ticker} по {targetPrice:C}";
        }

        /// <summary>
        /// Process pending orders for all players
        /// </summary>
        public void ProcessPendingOrders()
        {
            foreach (var player in _playerService.GetAllPlayers())
            {
                var ordersToExecute = new List<StockOrder>();

                foreach (var order in player.Portfolio.PendingOrders)
                {
                    var stock = Stocks.FirstOrDefault(s => s.Ticker == order.Ticker);
                    if (stock == null) continue;

                    bool shouldExecute = order.Type switch
                    {
                        OrderType.Limit when order.IsBuy => stock.SharePrice <= order.TargetPrice,
                        OrderType.Limit when !order.IsBuy => stock.SharePrice >= order.TargetPrice,
                        OrderType.StopLoss when !order.IsBuy => stock.SharePrice <= order.TargetPrice,
                        OrderType.StopLoss when order.IsBuy => stock.SharePrice >= order.TargetPrice,
                        _ => false
                    };

                    if (shouldExecute)
                    {
                        if (order.IsBuy)
                            BuyStock(player, order.Ticker, order.Quantity);
                        else
                            SellStock(player, order.Ticker, order.Quantity);
                        
                        ordersToExecute.Add(order);
                    }
                }

                // Remove executed orders
                foreach (var order in ordersToExecute)
                {
                    player.Portfolio.PendingOrders.Remove(order);
                }
            }
        }

        /// <summary>
        /// Pay dividends to all stockholders (called periodically)
        /// </summary>
        public void PayDividends(DateTime currentTime)
        {
            if ((currentTime - LastDividendPaid).Days < GameConstants.DividendPaymentDays) return;

            foreach (var player in _playerService.GetAllPlayers())
            {
                decimal totalDividends = 0;

                foreach (var holding in player.Portfolio.Holdings)
                {
                    var stock = Stocks.FirstOrDefault(s => s.Ticker == holding.Key);
                    if (stock == null) continue;

                    // Monthly dividend = (SharePrice * DividendYield) / 12 * shares
                    decimal monthlyDividend = stock.SharePrice * stock.DividendYield / 12m * holding.Value;
                    totalDividends += monthlyDividend;
                }

                if (totalDividends > 0)
                {
                    player.Money += totalDividends;
                    player.DividendIncome += totalDividends;
                    player.MonthlyIncome += totalDividends;
                }
            }

            LastDividendPaid = currentTime;
        }

        /// <summary>
        /// Cancel a pending order
        /// </summary>
        public string CancelOrder(Player player, Guid orderId)
        {
            if (player == null) return "Игрок не найден!";

            var order = player.Portfolio.PendingOrders.FirstOrDefault(o => o.OrderId == orderId);
            if (order == null) return "Ордер не найден!";

            player.Portfolio.PendingOrders.Remove(order);
            return $"Ордер отменён: {order.Ticker} x{order.Quantity}";
        }

        /// <summary>
        /// Calculate total portfolio value for a player
        /// </summary>
        public decimal GetPortfolioValue(Player player)
        {
            decimal total = 0;
            foreach (var holding in player.Portfolio.Holdings)
            {
                var stock = Stocks.FirstOrDefault(s => s.Ticker == holding.Key);
                if (stock != null) total += stock.SharePrice * holding.Value;
            }
            return total;
        }

        /// <summary>
        /// Calculate unrealized P&L for a player
        /// </summary>
        public decimal GetUnrealizedPnL(Player player)
        {
            decimal total = 0;
            foreach (var holding in player.Portfolio.Holdings)
            {
                var stock = Stocks.FirstOrDefault(s => s.Ticker == holding.Key);
                if (stock == null) continue;
                
                var avgBuy = player.Portfolio.AvgBuyPrice.GetValueOrDefault(holding.Key, stock.SharePrice);
                total += (stock.SharePrice - avgBuy) * holding.Value;
            }
            return total;
        }

        /// <summary>
        /// Get current stock for save/load
        /// </summary>
        public void SetStocks(List<Stock> stocks)
        {
            Stocks = stocks;
        }
    }
}

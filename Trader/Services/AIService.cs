using EconomicGame.Configuration;
using EconomicGame.Models;
using System;
using System.Linq;
using System.Threading.Tasks;


namespace EconomicGame.Services
{
    public class AIService
    {
        private readonly GameEngine _gameEngine;
        private readonly PlayerService _playerService;
        private readonly CorporateRivalryService _rivalryService;
        private readonly StockMarketService _stockMarketService;
        private static readonly Random _random = Random.Shared;

        public AIService(GameEngine gameEngine, PlayerService playerService, CorporateRivalryService rivalryService, StockMarketService stockMarketService)
        {
            _gameEngine = gameEngine;
            _playerService = playerService;
            _rivalryService = rivalryService;
            _stockMarketService = stockMarketService;

            InitializePopulation();
        }

        private void InitializePopulation()
        {
            var currentAIs = _playerService.GetAllPlayers().Where(p => p.IsAI).ToList();
            int targetPopulation = 100;

            if (currentAIs.Count < targetPopulation)
            {
                var baseStrategy = new GeneticStrategy();
                for (int i = currentAIs.Count; i < targetPopulation; i++)
                {
                    var name = $"X-Node-{i:D4}";
                    var ai = _playerService.CreateAIPlayer(name);
                    ai.Strategy = baseStrategy.Clone();
                    ai.Strategy.Mutate(_random, 0.3); // High initial entropy for divergence
                }
            }
        }

        public void ProcessAIPlayers()
        {
            var aiPlayers = _playerService.GetAllPlayers().Where(p => p.IsAI).ToList();

            // Phase 2: React to latest news before processing trades
            var news = _gameEngine.LatestNews;
            if (news != null)
            {
                foreach (var ai in aiPlayers)
                {
                    UpdateSentimentFromNews(ai, news);
                }
            }

            // Get current escalation level for adaptive behavior
            var escalationLevel = _rivalryService.CurrentEscalationLevel;

            foreach (var ai in aiPlayers)
            {
                ManageFinances(ai);
                ManageInfrastructure(ai);
                ExecuteTrades(ai, escalationLevel);
                
                // AI Stock Trading (Investor behavior)
                if (_random.NextDouble() < ai.Strategy.StockTradingDrive * GameConstants.AIStockTradeChance)
                {
                    ExecuteStockTrades(ai, escalationLevel);
                }
            }
        }

        private void UpdateSentimentFromNews(Player ai, News news)
        {
            var strat = ai.Strategy;
            // News impact is weighted by the agent's NewsSensitivity
            double impact = (double)strat.NewsSensitivity * 0.1;
            
            // Basic sentiment shift based on news type (simplified)
            // In a deeper system, we'd check if the news mentions items in the agent's specialization
            if (news.Title.Contains("Surge") || news.Title.Contains("Boom") || news.Title.Contains("Growth"))
                strat.Sentiment = Math.Min(1.0, strat.Sentiment + impact);
            else if (news.Title.Contains("Crash") || news.Title.Contains("Drop") || news.Title.Contains("Crisis"))
                strat.Sentiment = Math.Max(-1.0, strat.Sentiment - impact);
        }

        private void ManageFinances(Player ai)
        {
            var strat = ai.Strategy;

            // Take a loan if broke - threshold scaled by RiskTolerance
            decimal emergencyThreshold = 500 * (1 / Math.Max(0.1m, strat.RiskTolerance));
            
            if (ai.Money < emergencyThreshold && !ai.Loans.Any())
            {
                _gameEngine.Bank.TakeLoan(ai, 5000, 12, _gameEngine.CurrentTime);
            }

            // Repay loans if rich - conservative agents repay earlier
            decimal repayThreshold = 10000 * strat.RiskTolerance;
            if (ai.Money > repayThreshold && ai.Loans.Any())
            {
                var loan = ai.Loans.First();
                var totalDue = loan.Amount + loan.Penalty;
                if (ai.Money > totalDue + 2000)
                {
                    ai.Money -= totalDue;
                    ai.Loans.Remove(loan);
                }
            }
        }

        private void ManageInfrastructure(Player ai)
        {
            var strat = ai.Strategy;

            // AI only invests in infrastructure if they have a decent ProductionDrive and liquidity
            // Aggressive agents (high RiskTolerance) require less safety buffer
            decimal safetyBuffer = 25000m - (strat.RiskTolerance * 10000m);

            if (ai.Money > safetyBuffer && strat.ProductionDrive > 0.6)
            {
                if (ai.Land == null)
                {
                    _gameEngine.BuyLand(ai, LandType.SmallPlot);
                }
                else if (!ai.Warehouses.Any())
                {
                    _gameEngine.BuyWarehouse(ai, WarehouseType.MiniWarehouse);
                }
                // Wealthy AIs can upgrade to bigger warehouses
                else if (ai.Money > 100000m && ai.Warehouses.Count < ai.MaxWarehouses && strat.ProductionDrive > 0.8)
                {
                    _gameEngine.BuyWarehouse(ai, WarehouseType.Warehouse);
                }
            }
        }

        private void ExecuteTrades(Player ai, int escalationLevel)
        {
            if (ai.IsSabotaged) return;

            var exchangeItems = _gameEngine.ExchangeItems;
            var strat = ai.Strategy;

            // Escalation-driven aggression boost
            double aggressionBoost = 1.0 + (escalationLevel * strat.AggressionLevel * 0.3);
            bool isCoalitionAction = _rivalryService.IsCoalitionAction(ai);

            foreach (var item in exchangeItems)
            {
                decimal sma = item.CurrentPrice;
                int smaLength = (int)Math.Max(2, 5 * strat.TimeHorizon);

                if (item.PriceHistory.Count >= smaLength)
                {
                    sma = item.PriceHistory.TakeLast(smaLength).Average();
                }

                var existingItem = ai.Inventory.FirstOrDefault(i => i.ItemName == item.Name);

                // Phase 2 Sector Specialization
                bool isProduct = GameConstants.Products.Contains(item.Name);
                decimal specializationBias = isProduct ? strat.MarketSpecialization : (1.0m - strat.MarketSpecialization);
                decimal buyThreshold = isProduct ? strat.BuyThresholdProduct : strat.BuyThresholdRaw;
                
                // Adjust threshold based on specialization (lower threshold = more likely to buy)
                buyThreshold *= (1.0m - (specializationBias - 0.5m) * 0.1m);

                var player = _playerService.GetCurrentPlayer();

                if (ai.Money > GameConstants.AIMinCashBuffer && item.CurrentPrice < sma * buyThreshold)
                {
                    // Phase 6: Corporate Rivalry Buy logic (Raw Lockout)
                    if (player != null)
                    {
                        var multiplier = _rivalryService.GetRivalPriceAdjustment(ai, player, item);
                        if (multiplier > 1.0m) // Rival trying to overbid to starve player
                        {
                            buyThreshold *= multiplier; // Become 25% more willing to buy
                        }
                    }

                    // Phase 5: Scaled Trades for wealthy nodes
                    int baseQuantity = GameConstants.AIMaxQuantityPerTrade;
                    if (ai.Money > GameConstants.AIWealthyThreshold)
                    {
                        baseQuantity *= GameConstants.AIWealthyTradeMultiplier;
                    }

                    // Escalation boost: more aggressive buying at higher levels
                    baseQuantity = (int)(baseQuantity * aggressionBoost);

                    int affordable = (int)(ai.Money * GameConstants.AIPortfolioPercentPerTrade / item.CurrentPrice);
                    int quantityToBuy = Math.Min(affordable, baseQuantity);

                    // --- Phase 10: Bargain Hunting ---
                    if (item.PriceHistory.Count >= 10 && item.CurrentPrice < sma * 0.8m)
                    {
                        decimal boost = 1.0m + ((sma - item.CurrentPrice) / sma) * 5.0m;
                        boost = Math.Clamp(boost, 1.0m, 3.0m);
                        quantityToBuy = (int)(quantityToBuy * boost);
                        quantityToBuy = Math.Min(quantityToBuy, affordable);
                    }
                    
                    if (quantityToBuy > 0)
                    {
                        double rivalryBoost = 1.0;
                        if (player != null)
                        {
                            var playerInv = player.Inventory.FirstOrDefault(i => i.ItemName == item.Name);
                            if (playerInv != null && playerInv.Quantity > 500)
                            {
                                rivalryBoost = 1.2 + (escalationLevel * 0.1);
                            }
                        }

                        double adjustedEntropy = strat.TradeEntropy * (1.0 + strat.Sentiment * 0.5) * rivalryBoost * aggressionBoost;
                        
                        if (_random.NextDouble() < adjustedEntropy)
                        {
                            var oldBalance = ai.Money;
                            _gameEngine.BuyItem(ai, item, quantityToBuy);
                            if (ai.Money < oldBalance)
                            {
                                ai.TotalTrades++;
                            }
                        }
                    }
                }

                if (existingItem != null && existingItem.Quantity > 0)
                {
                    bool isProfitable = item.CurrentPrice > existingItem.AveragePrice * strat.ProfitThreshold;
                    bool isOvervalued = item.CurrentPrice > sma * strat.OvervaluedThreshold;
                    
                    // Phase 6: Corporate Rivalry Sell logic (Predatory Pricing)
                    if (player != null)
                    {
                        var multiplier = _rivalryService.GetRivalPriceAdjustment(ai, player, item);
                        if (multiplier < 1.0m) // Rival trying to undercut to crash market
                        {
                            isProfitable = true; // Sell even if not profitable to hurt player niche
                            isOvervalued = true;
                        }
                    }

                    double sellChance = (isProduct ? 0.8 : 0.6) * (1.0 - strat.Sentiment * 0.3) * aggressionBoost;
                    if (player != null)
                    {
                        var playerInv = player.Inventory.FirstOrDefault(i => i.ItemName == item.Name);
                        if (playerInv != null && playerInv.Quantity > 500)
                        {
                            sellChance *= 1.3 + (escalationLevel * 0.1);
                        }
                    }

                    if ((isProduct && isProfitable) || (!isProduct && (isProfitable || isOvervalued)))
                    {
                        if (_random.NextDouble() < sellChance)
                        {
                            int sellQty = existingItem.Quantity;

                            // Coalition dump: sell more to crash prices  
                            if (isCoalitionAction)
                            {
                                var dumpMultiplier = _rivalryService.GetCoalitionDumpMultiplier(ai);
                                // Can't sell more than we have, but signal more volume
                            }

                            _gameEngine.SellItem(ai, item, sellQty);
                            
                            ai.TotalTrades++;
                            if (item.CurrentPrice > existingItem.AveragePrice) ai.ProfitableTrades++;
                            ai.DailyProfit += (item.CurrentPrice - existingItem.AveragePrice) * sellQty;
                        }
                    }
                    else if (item.CurrentPrice < existingItem.AveragePrice * strat.StopLossThreshold)
                    {
                        if (_random.NextDouble() < 0.5 * (1.0 - strat.Sentiment)) 
                        {
                            int sellQty = existingItem.Quantity;
                            _gameEngine.SellItem(ai, item, sellQty);
                            
                            ai.TotalTrades++;
                            ai.DailyProfit += (item.CurrentPrice - existingItem.AveragePrice) * sellQty;
                        }
                    }
                }
            }
            
            TryProduction(ai);
            CheckTradeListings(ai);
        }

        /// <summary>
        /// AI stock trading: buy/sell stocks based on strategy and escalation.
        /// </summary>
        private void ExecuteStockTrades(Player ai, int escalationLevel)
        {
            var stocks = _stockMarketService.Stocks;
            var player = _playerService.GetCurrentPlayer();
            var strat = ai.Strategy;

            foreach (var stock in stocks)
            {
                // Buy stocks if price looks cheap vs history
                if (stock.PriceHistory.Count >= 5 && ai.Money > 5000m)
                {
                    var avgPrice = stock.PriceHistory.TakeLast(10).Average();
                    
                    // At higher escalation, bots specifically target stocks the player holds
                    bool playerHoldsThisStock = player != null && 
                        player.Portfolio.Holdings.ContainsKey(stock.Ticker) && 
                        player.Portfolio.Holdings[stock.Ticker] > 50;

                    decimal buyBelow = avgPrice * 0.95m; // Buy below 95% of avg
                    if (playerHoldsThisStock && escalationLevel >= 2)
                    {
                        buyBelow = avgPrice * 1.05m; // Buy even at premium to compete for shares
                    }

                    if (stock.SharePrice < buyBelow && stock.AvailableShares > 10)
                    {
                        int maxAffordable = (int)(ai.Money * 0.15m / stock.SharePrice);
                        int qty = Math.Min(Math.Min(maxAffordable, 50), stock.AvailableShares);
                        if (qty > 0)
                        {
                            _stockMarketService.BuyStock(ai, stock.Ticker, qty);
                        }
                    }
                }

                // Sell stocks if profitable
                if (ai.Portfolio.Holdings.ContainsKey(stock.Ticker) && ai.Portfolio.Holdings[stock.Ticker] > 0)
                {
                    var avgBuy = ai.Portfolio.AvgBuyPrice.GetValueOrDefault(stock.Ticker, stock.SharePrice);
                    
                    // Sell at 10% profit
                    if (stock.SharePrice > avgBuy * 1.10m)
                    {
                        if (_random.NextDouble() < 0.5)
                        {
                            int sellQty = ai.Portfolio.Holdings[stock.Ticker];
                            _stockMarketService.SellStock(ai, stock.Ticker, sellQty);
                        }
                    }
                    // Stop loss at 15%
                    else if (stock.SharePrice < avgBuy * 0.85m)
                    {
                        if (_random.NextDouble() < 0.3)
                        {
                            int sellQty = ai.Portfolio.Holdings[stock.Ticker];
                            _stockMarketService.SellStock(ai, stock.Ticker, sellQty);
                        }
                    }

                    // Coalition dump: sell stocks to crash prices of player's holdings
                    if (escalationLevel >= 3 && _rivalryService.IsCoalitionAction(ai))
                    {
                        if (player != null && player.Portfolio.Holdings.ContainsKey(stock.Ticker))
                        {
                            // Dump all shares to crash the price
                            if (ai.Portfolio.Holdings.ContainsKey(stock.Ticker))
                            {
                                int dumpQty = ai.Portfolio.Holdings[stock.Ticker];
                                if (dumpQty > 0)
                                {
                                    _stockMarketService.SellStock(ai, stock.Ticker, dumpQty);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void TryProduction(Player ai)
        {
            if (!ai.Warehouses.Any()) return;
            var strat = ai.Strategy;
            
            // Try Wheat -> Bread
            var wheat = ai.Inventory.FirstOrDefault(i => i.ItemName == GameConstants.Wheat);
            if (wheat != null && wheat.Quantity >= GameConstants.BreadWheatInput && ai.Money >= GameConstants.BreadProductionCost)
            {
                if (_random.NextDouble() < strat.ProductionDrive)
                {
                    wheat.Quantity -= GameConstants.BreadWheatInput;
                    if (wheat.Quantity <= 0) ai.Inventory.Remove(wheat);
                    ai.Money -= GameConstants.BreadProductionCost;
                    
                    var bread = ai.Inventory.FirstOrDefault(i => i.ItemName == GameConstants.Bread);
                    if (bread != null)
                    {
                        bread.Quantity += GameConstants.BreadOutput;
                    }
                    else
                    {
                        ai.Inventory.Add(new InventoryItem { ItemName = GameConstants.Bread, Quantity = GameConstants.BreadOutput, PurchasePrice = GameConstants.BreadProductionCost, AveragePrice = GameConstants.BreadProductionCost });
                    }

                    // Log production value as "profit" for metric tracking
                    ai.DailyProfit += 10; // Nominal value for bread production success
                }
            }
            
            // Try Steel + Oil -> Goods
            var steel = ai.Inventory.FirstOrDefault(i => i.ItemName == GameConstants.Steel);
            var oil = ai.Inventory.FirstOrDefault(i => i.ItemName == GameConstants.Oil);
            if (steel != null && steel.Quantity >= GameConstants.GoodsSteelInput && 
                oil != null && oil.Quantity >= GameConstants.GoodsOilInput && 
                ai.Money >= GameConstants.GoodsProductionCost)
            {
                if (_random.NextDouble() < strat.ProductionDrive * 0.8)
                {
                    steel.Quantity -= GameConstants.GoodsSteelInput;
                    if (steel.Quantity <= 0) ai.Inventory.Remove(steel);
                    oil.Quantity -= GameConstants.GoodsOilInput;
                    if (oil.Quantity <= 0) ai.Inventory.Remove(oil);
                    ai.Money -= GameConstants.GoodsProductionCost;
                    
                    var goods = ai.Inventory.FirstOrDefault(i => i.ItemName == GameConstants.Goods);
                    if (goods != null)
                    {
                        goods.Quantity += GameConstants.GoodsOutput;
                    }
                    else
                    {
                        ai.Inventory.Add(new InventoryItem { ItemName = GameConstants.Goods, Quantity = GameConstants.GoodsOutput, PurchasePrice = GameConstants.GoodsProductionCost, AveragePrice = GameConstants.GoodsProductionCost });
                    }
                    
                    ai.DailyProfit += 25; // Higher nominal value for complex goods
                }
            }

            // Try Oil + Copper -> Chemicals (new recipe)
            var copper = ai.Inventory.FirstOrDefault(i => i.ItemName == GameConstants.Copper);
            if (oil != null && oil.Quantity >= GameConstants.ChemicalsOilInput &&
                copper != null && copper.Quantity >= GameConstants.ChemicalsCopperInput &&
                ai.Money >= GameConstants.ChemicalsProductionCost)
            {
                if (_random.NextDouble() < strat.ProductionDrive * 0.6)
                {
                    oil.Quantity -= GameConstants.ChemicalsOilInput;
                    if (oil.Quantity <= 0) ai.Inventory.Remove(oil);
                    copper.Quantity -= GameConstants.ChemicalsCopperInput;
                    if (copper.Quantity <= 0) ai.Inventory.Remove(copper);
                    ai.Money -= GameConstants.ChemicalsProductionCost;

                    var chem = ai.Inventory.FirstOrDefault(i => i.ItemName == GameConstants.Chemicals);
                    if (chem != null)
                    {
                        chem.Quantity += GameConstants.ChemicalsOutput;
                    }
                    else
                    {
                        ai.Inventory.Add(new InventoryItem { ItemName = GameConstants.Chemicals, Quantity = GameConstants.ChemicalsOutput, PurchasePrice = GameConstants.ChemicalsProductionCost, AveragePrice = GameConstants.ChemicalsProductionCost });
                    }

                    ai.DailyProfit += 40;
                }
            }
        }

        private void CheckTradeListings(Player ai)
        {
            var listings = _gameEngine.TradeListings.ToList(); 
            
            foreach (var listing in listings)
            {
                if (listing.SellerId == ai.Id) continue; 

                var marketItem = _gameEngine.ExchangeItems.FirstOrDefault(i => i.Name == listing.ItemName);
                if (marketItem == null) continue;

                if (listing.PricePerUnit < marketItem.CurrentPrice * GameConstants.AIStopLossThreshold && ai.Money > listing.TotalPrice)
                {
                    if (_random.NextDouble() < GameConstants.AITradeChance)
                    {
                        var result = _gameEngine.BuyListing(ai, listing);
                    }
                }
            }
        }
    }
}

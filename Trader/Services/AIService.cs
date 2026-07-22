using EconomicGame.Configuration;
using EconomicGame.Models;
using System;
using System.Collections.Generic;
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
        private readonly CorporateActionService _actionService;
        private static readonly Random _random = Random.Shared;

        public AIService(GameEngine gameEngine, PlayerService playerService, CorporateRivalryService rivalryService, StockMarketService stockMarketService, CorporateActionService actionService)
        {
            _gameEngine = gameEngine;
            _playerService = playerService;
            _rivalryService = rivalryService;
            _stockMarketService = stockMarketService;
            _actionService = actionService;

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

            // Fetch the market items ONCE for the whole AI pass.
            var exchangeItems = _gameEngine.ExchangeItems;

            foreach (var ai in aiPlayers)
            {
                // Mood mean-reversion: without fresh news, sentiment slowly drifts back
                // to neutral instead of staying pinned at an extreme forever.
                // (Playtest showed the whole population stuck at -1.0 = permanent bear market.)
                ai.Strategy.Sentiment *= 0.995;

                ManageFinances(ai);
                ManageInfrastructure(ai);
                AnnounceMilestones(ai);
                ExecuteTrades(ai, escalationLevel, exchangeItems);
                
                // AI Stock Trading (Investor behavior)
                if (_random.NextDouble() < ai.Strategy.StockTradingDrive * GameConstants.AIStockTradeChance)
                {
                    ExecuteStockTrades(ai, escalationLevel);
                }

                // AI Corporate Retaliation (Sabotage)
                if (escalationLevel >= 2)
                {
                    ExecuteCorporateActions(ai, escalationLevel);
                }
            }
        }

        // --- Bot milestones: wealth announcements create competitive pressure ---
        private readonly Dictionary<Guid, decimal> _announcedMilestones = new();
        private static readonly decimal[] WealthMilestones = { 250000m, 1000000m, 5000000m };

        private void AnnounceMilestones(Player ai)
        {
            var netWorth = ai.NetWorth;
            decimal reached = 0;
            foreach (var m in WealthMilestones)
                if (netWorth >= m) reached = m;

            if (reached > 0 && reached > _announcedMilestones.GetValueOrDefault(ai.Id))
            {
                _announcedMilestones[ai.Id] = reached;
                string title = reached switch
                {
                    5000000m => $"👑 {ai.Name} построил империю на {reached:C0}!",
                    1000000m => $"🏆 {ai.Name} стал МИЛЛИОНЕРОМ! Рынок обсуждает новую звезду.",
                    _ => $"📈 {ai.Name} перешагнул капитал {reached:C0} и явно не собирается останавливаться."
                };
                _gameEngine.LogActivity(title);
            }
        }

        private void UpdateSentimentFromNews(Player ai, News news)
        {
            var strat = ai.Strategy;
            // News impact is weighted by the agent's NewsSensitivity
            double impact = (double)strat.NewsSensitivity * 0.1;
            
            // Basic sentiment shift based on news type (simplified)
            // In a deeper system, we'd check if the news mentions items in the agent's specialization
            string[] positive = { "Surge", "Boom", "Growth", "Demand", "Rally", "Flock", "Rise" };
            string[] negative = { "Crash", "Drop", "Crisis", "Plunge", "Fall", "Collapse" };

            if (positive.Any(w => news.Title.Contains(w, StringComparison.OrdinalIgnoreCase)))
                strat.Sentiment = Math.Min(1.0, strat.Sentiment + impact);
            else if (negative.Any(w => news.Title.Contains(w, StringComparison.OrdinalIgnoreCase)))
                strat.Sentiment = Math.Max(-1.0, strat.Sentiment - impact);
        }

        private void ManageFinances(Player ai)
        {
            var strat = ai.Strategy;

            // Nobody wants to sleep in the car (homeless penalty applies to bots too):
            // grab at least a small room as soon as there's spare cash.
            if (!ai.Properties.Any() && ai.Money > 15000m)
            {
                _gameEngine.BuyProperty(ai, PropertyType.SmallRoom);
            }

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

        /// <summary>
        /// The full infrastructure tech tree for bots (bot-competitors feature):
        ///   land → warehouse → trading license → plantation/farm → processing factory
        ///   → auto-production → bigger warehouses/licenses.
        /// Bots pay the SAME prices, maintenance and license limits as the human player —
        /// no cheats. Only genuinely production-minded bots (ProductionDrive > 0.7,
        /// roughly 15-25% of the population) go deep into the producer path; their crop
        /// specialization comes from the MarketSpecialization gene, so the 100 nodes
        /// don't all pile into sugar and crash one market.
        /// </summary>
        private void ManageInfrastructure(Player ai)
        {
            var strat = ai.Strategy;

            // Aggressive agents (high RiskTolerance) require less safety buffer
            decimal safetyBuffer = 25000m - (strat.RiskTolerance * 10000m);
            if (ai.Money <= safetyBuffer || strat.ProductionDrive <= 0.6) return;

            // --- Step 1: land ---
            if (!ai.Lands.Any())
            {
                _gameEngine.BuyLand(ai, LandType.SmallPlot);
                return;
            }

            // --- Step 2: first warehouse ---
            if (!ai.Warehouses.Any())
            {
                _gameEngine.BuyWarehouse(ai, WarehouseType.MiniWarehouse);
                return;
            }

            bool deepProducer = strat.ProductionDrive > 0.7;

            // --- Step 3: trading license L1 (unlocks more land/warehouses) ---
            if (deepProducer && ai.TradingLicenseLevel == 0 && ai.Money > 60000m)
            {
                _gameEngine.BuyTradingLicense(ai, 1);
                return;
            }

            // --- Step 4: agriculture, specialized by the MarketSpecialization gene ---
            if (deepProducer && ai.Money > 130000m)
            {
                bool hasFarm = ai.Factories.Any(f =>
                    f.Type is FactoryType.SugarCanePlantation or FactoryType.CoffeePlantation or FactoryType.WheatFarm);
                if (!hasFarm)
                {
                    var farm = strat.MarketSpecialization switch
                    {
                        < 0.35m => FactoryType.WheatFarm,
                        > 0.70m => FactoryType.CoffeePlantation,
                        _ => FactoryType.SugarCanePlantation
                    };
                    _gameEngine.BuyFactory(ai, farm);
                    RegisterAutoRecipe(ai, farm);
                    return;
                }

                // --- Step 5: processing factory to match the crop ---
                if (ai.Money > 400000m)
                {
                    var crop = ai.Factories.First(f =>
                        f.Type is FactoryType.SugarCanePlantation or FactoryType.CoffeePlantation or FactoryType.WheatFarm).Type;
                    var processor = crop switch
                    {
                        FactoryType.SugarCanePlantation => FactoryType.SugarRefinery,
                        FactoryType.CoffeePlantation => FactoryType.TextileMill, // coffee bots diversify into textiles (wheat+coffee recipe)
                        _ => FactoryType.SteelMill
                    };
                    if (!ai.Factories.Any(f => f.Type == processor))
                    {
                        _gameEngine.BuyFactory(ai, processor);
                        return;
                    }
                }
            }

            // --- Step 6: warehouse expansion for the wealthy ---
            if (ai.Money > 100000m && ai.Warehouses.Count < ai.MaxWarehouses && strat.ProductionDrive > 0.8)
            {
                _gameEngine.BuyWarehouse(ai, WarehouseType.Warehouse);
                return;
            }

            // --- Step 7: higher licenses for empires ---
            if (deepProducer && ai.TradingLicenseLevel == 1 && ai.Money > 300000m)
            {
                _gameEngine.BuyTradingLicense(ai, 2);
            }
        }

        /// <summary>
        /// Signs the bot up for the auto-production recipe matching its crop —
        /// the same auto-production pipeline (with real recipe times) the player uses.
        /// </summary>
        private static void RegisterAutoRecipe(Player ai, FactoryType farm)
        {
            string recipeName = farm switch
            {
                FactoryType.SugarCanePlantation => "Сахар",
                FactoryType.CoffeePlantation => "Кофе",
                _ => "Хлеб"
            };
            var recipe = ProductionRecipes.AllRecipes.FirstOrDefault(r => r.Name == recipeName);
            if (recipe != null && !ai.AutoProductionRecipes.Contains(recipe.RecipeId))
            {
                ai.AutoProductionRecipes.Add(recipe.RecipeId);
            }
        }

        private void ExecuteTrades(Player ai, int escalationLevel, List<MarketItem>? cachedExchangeItems = null)
        {
            if (ai.IsSabotaged) return;

            // Prefer the caller-cached item list to avoid 100× ToList() per tick.
            var exchangeItems = cachedExchangeItems ?? _gameEngine.ExchangeItems;
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
                                // No wash-trading: an item bought this tick is not sold this tick.
                                // The buy window (< SMA*1.05) and the "overvalued" sell window
                                // (> SMA*1.05) touch at the boundary, and without this guard bots
                                // would buy and immediately dump the same goods at the same price.
                                continue;
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
                            // Sell a fraction of the position rather than dumping it whole.
                            // This smooths volume impact on the exchange and matches how the
                            // buy side already operates. Aggressive / coalition bots sell more.
                            decimal sellFraction = isCoalitionAction
                                ? Math.Min(1.0m, _rivalryService.GetCoalitionDumpMultiplier(ai) * 0.25m)
                                : 0.25m + (decimal)strat.AggressionLevel * 0.25m; // 25%-50%
                            int sellQty = Math.Max(1, (int)(existingItem.Quantity * sellFraction));
                            sellQty = Math.Min(sellQty, existingItem.Quantity);

                            var oldQty = existingItem.Quantity;
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
                            // Stop-loss is an "all out" event — protect what's left of capital.
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
                    
                    // Sell a fraction of the position rather than dumping it whole.
                    // This smooths volume impact on the stock exchange.
                    decimal sellFraction = 0.25m + (decimal)strat.AggressionLevel * 0.25m; // 25%-50%
                    int totalHeld = ai.Portfolio.Holdings[stock.Ticker];
                    int sellQty = Math.Max(1, (int)(totalHeld * sellFraction));
                    sellQty = Math.Min(sellQty, totalHeld);

                    // Sell at 10% profit
                    if (stock.SharePrice > avgBuy * 1.10m)
                    {
                        if (_random.NextDouble() < 0.5)
                        {
                            _stockMarketService.SellStock(ai, stock.Ticker, sellQty);
                        }
                    }
                    // Stop loss at 15%
                    else if (stock.SharePrice < avgBuy * 0.85m)
                    {
                        if (_random.NextDouble() < 0.3)
                        {
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
            // Cache once per pass to avoid re-copying ExchangeItems for every listing.
            var exchangeItems = _gameEngine.ExchangeItems;

            // Bargain definition: listing must be at least ~8% below the live market price.
            // (The old code reused AIStopLossThreshold here, which was semantically wrong.)
            const decimal BargainDiscount = 0.92m;

            foreach (var listing in listings)
            {
                if (listing.SellerId == ai.Id) continue;

                var marketItem = exchangeItems.FirstOrDefault(i => i.Name == listing.ItemName);
                if (marketItem == null) continue;

                // Must have enough money AND enough cargo space — previously the AI would try
                // to snatch listings it had nowhere to store, silently failing in BuyListing.
                if (ai.Money <= listing.TotalPrice) continue;
                if (listing.Quantity > ai.AvailableCargoSpace) continue;

                if (listing.PricePerUnit < marketItem.CurrentPrice * BargainDiscount)
                {
                    if (_random.NextDouble() < GameConstants.AITradeChance)
                    {
                        _gameEngine.BuyListing(ai, listing);
                    }
                }
            }
        }

        private void ExecuteCorporateActions(Player ai, int escalationLevel)
        {
            var userPlayer = _playerService.GetCurrentPlayer();
            if (userPlayer == null || userPlayer.IsSabotaged) return;

            // Bots only sabotage if you are their rival and threat is high
            if (userPlayer.RivalPlayerIds.Contains(ai.Id))
            {
                // Chance to sabotage based on aggression and escalation
                double sabotageChance = 0.005 * escalationLevel * ai.Strategy.AggressionLevel;
                
                // Coalition members are more likely to coordinate sabotage
                if (_rivalryService.IsCoalitionAction(ai))
                {
                    sabotageChance *= 2.0;
                }

                if (_random.NextDouble() < sabotageChance)
                {
                    var result = _actionService.SabotageRival(userPlayer.Id, _gameEngine.CurrentTime, ai.Id);
                    if (result.Contains("Успех"))
                    {
                        _gameEngine.TriggerStateChanged();
                    }
                    else if (userPlayer.TradingLicenseLevel > 0)
                    {
                        _gameEngine.LogActivity($"🛡️ {userPlayer.Name} успешно отразил попытку саботажа от {ai.Name} благодаря торговой лицензии!");
                        _gameEngine.TriggerStateChanged();
                    }
                }
            }
        }
    }
}

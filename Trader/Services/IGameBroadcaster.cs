using System.Collections.Generic;

namespace EconomicGame.Services
{
    /// <summary>
    /// Abstraction over "push game updates to external clients".
    /// The Blazor SERVER host implements this with SignalR (GameHub);
    /// the Blazor WEBASSEMBLY host uses a no-op — everything runs in-process
    /// in the browser and components already refresh via GameEngine.OnStateChanged.
    /// This keeps GameEngine free of ASP.NET server-only dependencies.
    /// </summary>
    public interface IGameBroadcaster
    {
        void BroadcastPrices(List<MarketItem> items);
        void BroadcastNews(News? news);
    }

    /// <summary>No-op broadcaster for hosts without external clients (WASM).</summary>
    public class NullGameBroadcaster : IGameBroadcaster
    {
        public void BroadcastPrices(List<MarketItem> items) { }
        public void BroadcastNews(News? news) { }
    }
}

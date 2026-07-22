using System.Collections.Generic;
using EconomicGame.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace EconomicGame.Services
{
    /// <summary>
    /// Server-host implementation of IGameBroadcaster: pushes updates
    /// to connected SignalR clients through GameHub.
    /// </summary>
    public class SignalRGameBroadcaster : IGameBroadcaster
    {
        private readonly IHubContext<GameHub> _hubContext;

        public SignalRGameBroadcaster(IHubContext<GameHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public void BroadcastPrices(List<MarketItem> items) =>
            _hubContext.Clients.All.SendAsync("PriceUpdate", items);

        public void BroadcastNews(News? news) =>
            _hubContext.Clients.All.SendAsync("NewsUpdate", news);
    }
}

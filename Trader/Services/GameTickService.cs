using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using EconomicGame.Configuration;

namespace EconomicGame.Services
{
    public class GameTickService : BackgroundService
    {
        private readonly GameEngine _gameEngine;
        private readonly AIService _aiService;

        public GameTickService(GameEngine gameEngine, AIService aiService)
        {
            _gameEngine = gameEngine;
            _aiService = aiService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(GameConstants.GameTickIntervalSeconds));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    _gameEngine.UpdateGameState();
                    _aiService.ProcessAIPlayers();
                }
                catch (Exception ex)
                {
                    // Log error (you can add ILogger here if needed)
                    Console.WriteLine($"Error in game tick: {ex}");
                }
            }
        }
    }
}

using System.Globalization;
using EconomicGame.Configuration;
using EconomicGame.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Trader.Components;

// ============================================================================
// Trader — Blazor WebAssembly host.
// The ENTIRE game (engine, 100 AI bots, poker) runs inside the browser tab:
// no server, no backend — perfect for GitHub Pages and playing on a phone.
// ============================================================================

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same service graph as the server host, minus server-only pieces:
// SignalR broadcaster → no-op, file saves → browser localStorage.
builder.Services.AddSingleton<IGameBroadcaster, NullGameBroadcaster>();
builder.Services.AddSingleton<ISaveStorage, LocalStorageSaveStorage>();
builder.Services.AddSingleton<PlayerService>();
builder.Services.AddSingleton<StockMarketService>();
builder.Services.AddSingleton<SyncEngine>();
builder.Services.AddSingleton<CorporateRivalryService>();
builder.Services.AddSingleton<CorporateActionService>();
builder.Services.AddSingleton<InsuranceService>();
builder.Services.AddSingleton<ScenarioService>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<AIService>();
builder.Services.AddSingleton<PokerService>();
builder.Services.AddSingleton<SaveGameService>();

// Localization is Scoped on the server (one per circuit); in WASM there is
// exactly one user per app instance, so Scoped behaves the same.
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<NavigationStateService>();

// Consistent currency formatting ($) regardless of the visitor's locale
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

var host = builder.Build();

// Game tick loop — replaces the server's GameTickService (IHostedService
// does not run in Blazor WebAssembly).
var engine = host.Services.GetRequiredService<GameEngine>();
var ai = host.Services.GetRequiredService<AIService>();
_ = RunGameLoopAsync(engine, ai);

await host.RunAsync();

static async Task RunGameLoopAsync(GameEngine engine, AIService ai)
{
    var timer = new PeriodicTimer(TimeSpan.FromSeconds(GameConstants.GameTickIntervalSeconds));
    while (await timer.WaitForNextTickAsync())
    {
        try
        {
            engine.UpdateGameState();
            ai.ProcessAIPlayers();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in game tick: {ex.Message}");
        }
    }
}

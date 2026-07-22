using EconomicGame;
using EconomicGame.Services;
using EconomicGame.Hubs;
using Microsoft.AspNetCore.SignalR;
using Trader.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR(); // Keep for GameHub

// Game services
builder.Services.AddSingleton<EconomicGame.Services.IGameBroadcaster, EconomicGame.Services.SignalRGameBroadcaster>();
builder.Services.AddSingleton<EconomicGame.Services.ISaveStorage, EconomicGame.Services.FileSaveStorage>();
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
builder.Services.AddHostedService<GameTickService>();

// Localization is Scoped — one instance per Blazor circuit so each
// connected user keeps their own language preference.
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<NavigationStateService>();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<EconomicGame.Hubs.GameHub>("/gameHub");

app.Run();
using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Configuration;
using Orchestrator.Application.Contracts;
using Orchestrator.Application.Contracts.Persistence;
using Orchestrator.Application.Options;
using Orchestrator.Execution;
using Orchestrator.Execution.Indicators;
using Orchestrator.Execution.Orders;
using Orchestrator.Execution.Risk;
using Orchestrator.Infra;
using Orchestrator.Market;
using Orchestrator.Providers;
using Orchestrator.Observability;
using Orchestrator.Realtime;
using Orchestrator.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

builder.Services.Configure<TradingOptions>(builder.Configuration.GetSection("Trading"));
builder.Services.Configure<ProviderOptions>(builder.Configuration.GetSection("Providers"));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));

builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<IMarketDataCache, MarketDataCache>();
builder.Services.AddSingleton<IPriceHistoryStore, PriceHistoryStore>();
builder.Services.AddSingleton<IFreeModeConfigProvider, FreeModeConfigProvider>();
builder.Services.AddSingleton<IConfigureOptions<TradingOptions>, FreeModeTradingOptionsConfigurator>();
builder.Services.AddSingleton<IRiskManager, DefaultRiskManager>();
builder.Services.AddSingleton<IBrokerAdapter, PaperBroker>();
builder.Services.AddSingleton<IStrategyHost, StrategyHost>();
builder.Services.AddSingleton<IPortfolioService, PortfolioService>();
builder.Services.AddSingleton<IIndicatorSnapshotStore, IndicatorSnapshotStore>();
builder.Services.AddSingleton<IOrderStore, InMemoryOrderStore>();
builder.Services.AddSingleton<WebSocketConnectionManager>();
builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddSingleton<ITradingDataStore, DapperTradingDataStore>();
builder.Services.AddHttpClient("AlphaVantage", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("BinancePublic", client =>
{
    client.BaseAddress = new Uri("https://api.binance.com");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHostedService<BinanceWebSocketService>();
builder.Services.AddHostedService<FinnhubWebSocketService>();
builder.Services.AddHostedService<AlphaVantagePollingService>();
builder.Services.AddHostedService<WebSocketBroadcastService>();
builder.Services.AddHostedService<MetricsSubscriptionService>();
builder.Services.AddHostedService<PersistenceSubscriptionService>();
builder.Services.AddHostedService<PriceHistoryProjection>();

const string UiCorsPolicy = "ui-cors";

var corsSection = builder.Configuration.GetSection("Cors");
var allowedOrigins = corsSection.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var allowCredentials = corsSection.GetValue("AllowCredentials", false);

builder.Services.AddCors(options =>
{
    options.AddPolicy(UiCorsPolicy, policy =>
    {
        var normalizedOrigins = allowedOrigins
            .Select(o => (o ?? string.Empty).Trim())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(normalizedOrigins);
            if (!allowCredentials)
            {
                policy.AllowAnyHeader().AllowAnyMethod();
            }
            else
            {
                policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
        }

        policy.WithExposedHeaders("Content-Disposition");
    });
});

builder.Services.AddControllers();

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors(UiCorsPolicy);
app.UseWebSockets();

app.MapHealthChecks("/health");
app.MapControllers();
app.MapGet("/metrics", async context =>
{
    var collector = context.RequestServices.GetRequiredService<IMetricsCollector>();
    context.Response.ContentType = "text/plain; version=0.0.4";
    await context.Response.WriteAsync(collector.RenderSnapshot(), context.RequestAborted);
});
app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var manager = context.RequestServices.GetRequiredService<WebSocketConnectionManager>();
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = manager.Register(socket);
    await manager.ListenAsync(connectionId, socket, context.RequestAborted);
});

var strategyHost = app.Services.GetRequiredService<IStrategyHost>();
await strategyHost.InitializeAsync();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogInformation("Orchestrator started in {Environment}", app.Environment.EnvironmentName);
});

await app.RunAsync();

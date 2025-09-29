using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Application.Contracts;
using Orchestrator.Infra;
using Orchestrator.Market;

namespace Orchestrator.Realtime;

public sealed class WebSocketBroadcastService : IHostedService, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly WebSocketConnectionManager _connections;
    private readonly ILogger<WebSocketBroadcastService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly List<IDisposable> _subscriptions = new();

    public WebSocketBroadcastService(IEventBus eventBus, WebSocketConnectionManager connections, ILogger<WebSocketBroadcastService> logger)
    {
        _eventBus = eventBus;
        _connections = connections;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(_eventBus.Subscribe<StrategySignalEvent>(OnSignalAsync));
        _subscriptions.Add(_eventBus.Subscribe<ExecutionFillEvent>(OnExecutionAsync));
        _subscriptions.Add(_eventBus.Subscribe<AlertEvent>(OnAlertAsync));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        return Task.CompletedTask;
    }

    private async ValueTask OnSignalAsync(StrategySignalEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["channel"] = "signals",
                ["ts"] = evt.TimestampUtc.ToString("O"),
                ["instrument"] = evt.Signal.Instrument,
                ["strategy"] = evt.Signal.Strategy,
                ["score"] = evt.Signal.Score,
                ["features"] = evt.Signal.Features,
                ["recommended"] = new Dictionary<string, object?>
                {
                    ["side"] = evt.Signal.Side == TradeSide.Buy ? "BUY" : "SELL",
                    ["entry"] = evt.Signal.EntryPrice,
                    ["stop"] = evt.Signal.StopPrice,
                    ["target"] = evt.Signal.TargetPrice,
                    ["risk_pct"] = evt.Signal.RiskFraction
                }
            }, _jsonOptions);

            await _connections.BroadcastAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast signal event");
        }
    }

    private async ValueTask OnExecutionAsync(ExecutionFillEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["channel"] = "executions",
                ["ts"] = evt.TimestampUtc.ToString("O"),
                ["order_id"] = evt.OrderId,
                ["exec_id"] = evt.Fill.TradeId,
                ["instrument"] = evt.Fill.Instrument,
                ["price"] = evt.Fill.Price,
                ["qty"] = evt.Fill.Quantity,
                ["status"] = "FILLED",
                ["liquidity"] = null
            }, _jsonOptions);

            await _connections.BroadcastAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast execution event");
        }
    }

    private async ValueTask OnAlertAsync(AlertEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["channel"] = "alerts",
                ["ts"] = evt.TimestampUtc.ToString("O"),
                ["type"] = evt.Type,
                ["severity"] = evt.Severity,
                ["detail"] = evt.Detail,
                ["context"] = evt.Context
            }, _jsonOptions);

            await _connections.BroadcastAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast alert event");
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}

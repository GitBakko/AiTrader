using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Contracts;
using Orchestrator.Application.Contracts.Persistence;
using Orchestrator.Application.Options;
using Orchestrator.Execution.Orders;
using Orchestrator.Infra;
using Orchestrator.Market;

namespace Orchestrator.Persistence;

public sealed class PersistenceSubscriptionService : IHostedService, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly ITradingDataStore _dataStore;
    private readonly IOrderStore _orderStore;
    private readonly IPortfolioService _portfolio;
    private readonly IOptionsMonitor<TradingOptions> _tradingOptions;
    private readonly ILogger<PersistenceSubscriptionService> _logger;
    private readonly List<IDisposable> _subscriptions = new();

    public PersistenceSubscriptionService(
        IEventBus eventBus,
        ITradingDataStore dataStore,
        IOrderStore orderStore,
        IPortfolioService portfolio,
        IOptionsMonitor<TradingOptions> tradingOptions,
        ILogger<PersistenceSubscriptionService> logger)
    {
        _eventBus = eventBus;
        _dataStore = dataStore;
        _orderStore = orderStore;
        _portfolio = portfolio;
        _tradingOptions = tradingOptions;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(_eventBus.Subscribe<StrategySignalEvent>(OnSignalAsync));
        _subscriptions.Add(_eventBus.Subscribe<ExecutionFillEvent>(OnExecutionAsync));
        _subscriptions.Add(_eventBus.Subscribe<AlertEvent>(OnAlertAsync));
        _logger.LogInformation("Persistence subscriptions active");
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
            var record = new SignalRecord(
                evt.TimestampUtc,
                evt.Signal.Instrument,
                evt.Signal.Strategy,
                evt.Signal.Side,
                evt.Signal.Score,
                evt.Signal.RiskFraction,
                evt.Signal.EntryPrice,
                evt.Signal.StopPrice,
                evt.Signal.TargetPrice,
                null,
                evt.Signal.Features,
                null,
                new Dictionary<string, object>
                {
                    ["source"] = "strategy-host"
                });

            await _dataStore.InsertSignalAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist signal for {Instrument}", evt.Signal.Instrument);
        }
    }

    private async ValueTask OnExecutionAsync(ExecutionFillEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            var order = _orderStore.GetById(evt.OrderId);
            if (order is null)
            {
                _logger.LogWarning("Order {OrderId} not found in store; skipping persistence", evt.OrderId);
                return;
            }

            var riskFraction = order.RiskFraction > 0m
                ? order.RiskFraction
                : _tradingOptions.CurrentValue.Risk?.PerTradePct ?? 0.0035m;

            var tradeRecord = new TradeRecord(
                evt.Fill.TradeId,
                evt.Fill.Instrument,
                evt.Strategy,
                evt.Fill.Side,
                evt.Fill.Quantity,
                order.EntryPrice,
                order.StopPrice,
                order.TargetPrice,
                riskFraction,
                evt.Fill.TimestampUtc,
                evt.OrderId,
                order.CorrelationId,
                null);

            await _dataStore.InsertTradeAsync(tradeRecord, cancellationToken).ConfigureAwait(false);

            var executionRecord = new ExecutionRecord(
                evt.Fill.TradeId,
                evt.Fill.Instrument,
                evt.Fill.Side,
                evt.Fill.Price,
                evt.Fill.Quantity,
                evt.Fill.TimestampUtc,
                evt.OrderId,
                order.Source,
                order.Source,
                null,
                null,
                order.Status.ToString(),
                null,
                order.Metadata);

            await _dataStore.InsertExecutionAsync(executionRecord, cancellationToken).ConfigureAwait(false);

            var snapshot = _portfolio.GetSnapshot();
            var snapshotRecord = new PortfolioSnapshotRecord(
                DateTime.UtcNow,
                snapshot.Equity,
                snapshot.Equity,
                0m,
                0m,
                null,
                null,
                null);

            await _dataStore.InsertPortfolioSnapshotAsync(snapshotRecord, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist execution for {Instrument}", evt.Fill.Instrument);
        }
    }

    private async ValueTask OnAlertAsync(AlertEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = evt.Context is null ? null : new Dictionary<string, object>(evt.Context);

            var auditRecord = new AuditLogRecord(
                evt.TimestampUtc,
                "alert",
                evt.Severity,
                evt.Detail,
                null,
                null,
                metadata is not null && metadata.TryGetValue("instrument", out var instrumentObj) ? Convert.ToString(instrumentObj) : null,
                metadata is not null && metadata.TryGetValue("tradeId", out var tradeObj) ? Convert.ToString(tradeObj) : null,
                metadata);

            await _dataStore.InsertAuditLogAsync(auditRecord, cancellationToken).ConfigureAwait(false);

            if (IsRiskEvent(evt.Type))
            {
                var symbol = metadata is not null && metadata.TryGetValue("instrument", out var symObj)
                    ? Convert.ToString(symObj)
                    : null;

                var riskRecord = new RiskEventRecord(
                    evt.TimestampUtc,
                    evt.Type,
                    evt.Severity,
                    symbol,
                    metadata is not null && metadata.TryGetValue("tradeId", out var riskTradeObj) ? Convert.ToString(riskTradeObj) : null,
                    metadata is not null && metadata.TryGetValue("correlationId", out var corrObj) ? Convert.ToString(corrObj) : null,
                    evt.Detail,
                    metadata is not null ? JsonSerializer.Serialize(metadata) : null);

                await _dataStore.InsertRiskEventAsync(riskRecord, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist alert {Type}", evt.Type);
        }
    }

    private static bool IsRiskEvent(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return type.Contains("RISK", StringComparison.OrdinalIgnoreCase)
               || type.Contains("REJECT", StringComparison.OrdinalIgnoreCase)
               || type.Contains("STOP", StringComparison.OrdinalIgnoreCase)
               || type.Contains("CIRCUIT", StringComparison.OrdinalIgnoreCase);
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

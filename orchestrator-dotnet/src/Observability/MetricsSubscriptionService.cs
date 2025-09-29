using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Application.Contracts;
using Orchestrator.Infra;
using Orchestrator.Market;

namespace Orchestrator.Observability;

public sealed class MetricsSubscriptionService : IHostedService, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly IMetricsCollector _metrics;
    private readonly ILogger<MetricsSubscriptionService> _logger;
    private readonly List<IDisposable> _subscriptions = new();

    public MetricsSubscriptionService(IEventBus eventBus, IMetricsCollector metrics, ILogger<MetricsSubscriptionService> logger)
    {
        _eventBus = eventBus;
        _metrics = metrics;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(_eventBus.Subscribe<StrategySignalEvent>(OnSignalAsync));
        _subscriptions.Add(_eventBus.Subscribe<ExecutionFillEvent>(OnExecutionAsync));
        _subscriptions.Add(_eventBus.Subscribe<AlertEvent>(OnAlertAsync));
        _logger.LogInformation("Metrics subscription active");
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

    private ValueTask OnSignalAsync(StrategySignalEvent evt, CancellationToken cancellationToken)
    {
        _metrics.IncrementSignal(evt.Signal.Strategy);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnExecutionAsync(ExecutionFillEvent evt, CancellationToken cancellationToken)
    {
        _metrics.IncrementExecution(evt.Fill.Instrument);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnAlertAsync(AlertEvent evt, CancellationToken cancellationToken)
    {
        _metrics.IncrementAlert(evt.Type);
        return ValueTask.CompletedTask;
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

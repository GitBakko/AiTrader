using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using Orchestrator.Application.Contracts;

namespace Orchestrator.Observability;

public sealed class MetricsCollector : IMetricsCollector
{
    private readonly ConcurrentDictionary<string, long> _signals = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _executions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _alerts = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _buildInfo;

    public MetricsCollector()
    {
        _buildInfo = $"orchestrator_build_info{{version=\"0.8.1\"}} 1\n";
    }

    public void IncrementSignal(string strategy)
    {
        _signals.AddOrUpdate(strategy, 1, static (_, v) => v + 1);
    }

    public void IncrementExecution(string instrument)
    {
        _executions.AddOrUpdate(instrument, 1, static (_, v) => v + 1);
    }

    public void IncrementAlert(string type)
    {
        _alerts.AddOrUpdate(type, 1, static (_, v) => v + 1);
    }

    public string RenderSnapshot()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HELP orchestrator_build_info Build metadata");
        sb.AppendLine("# TYPE orchestrator_build_info gauge");
        sb.Append(_buildInfo);

        sb.AppendLine("# HELP orchestrator_signals_total Total strategy signals");
        sb.AppendLine("# TYPE orchestrator_signals_total counter");
        foreach (var pair in _signals.OrderBy(k => k.Key))
        {
            sb.Append("orchestrator_signals_total{strategy=\"")
                .Append(pair.Key)
                .Append("\"} ")
                .Append(pair.Value)
                .Append('\n');
        }

        sb.AppendLine("# HELP orchestrator_executions_total Total paper executions");
        sb.AppendLine("# TYPE orchestrator_executions_total counter");
        foreach (var pair in _executions.OrderBy(k => k.Key))
        {
            sb.Append("orchestrator_executions_total{instrument=\"")
                .Append(pair.Key)
                .Append("\"} ")
                .Append(pair.Value)
                .Append('\n');
        }

        sb.AppendLine("# HELP orchestrator_alerts_total Total alerts emitted");
        sb.AppendLine("# TYPE orchestrator_alerts_total counter");
        foreach (var pair in _alerts.OrderBy(k => k.Key))
        {
            sb.Append("orchestrator_alerts_total{type=\"")
                .Append(pair.Key)
                .Append("\"} ")
                .Append(pair.Value)
                .Append('\n');
        }

        return sb.ToString();
    }
}

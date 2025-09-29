using System.Collections.Concurrent;
using Orchestrator.Application.Contracts;

namespace Orchestrator.Execution.Indicators;

public sealed class IndicatorSnapshotStore : IIndicatorSnapshotStore
{
    private readonly ConcurrentDictionary<string, IndicatorSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public void Update(string symbol, IndicatorSnapshot snapshot)
    {
        _snapshots[symbol] = snapshot;
    }

    public IndicatorSnapshot? GetLatest(string symbol)
    {
        return _snapshots.TryGetValue(symbol, out var snapshot) ? snapshot : null;
    }
}

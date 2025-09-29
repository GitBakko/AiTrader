using System;
using System.Collections.Concurrent;
using Orchestrator.Application.Contracts;

namespace Orchestrator.Execution;

public sealed class PortfolioService : IPortfolioService
{
    private const decimal InitialEquity = 100000m;
    private readonly ConcurrentDictionary<string, decimal> _positions = new(StringComparer.OrdinalIgnoreCase);

    public AccountSnapshot GetSnapshot()
    {
        return new AccountSnapshot(InitialEquity, 0m, 0m, _positions.Count);
    }

    public void ApplyFill(Fill fill)
    {
        var delta = fill.Side == TradeSide.Buy ? fill.Quantity : -fill.Quantity;
        _positions.AddOrUpdate(fill.Instrument, delta, (_, existing) => existing + delta);

        if (_positions.TryGetValue(fill.Instrument, out var qty) && Math.Abs(qty) < 1e-6m)
        {
            _positions.TryRemove(fill.Instrument, out _);
        }
    }
}

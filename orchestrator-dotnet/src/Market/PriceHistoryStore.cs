using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Orchestrator.Market;

public interface IPriceHistoryStore
{
    IReadOnlyList<PriceBar> GetHistory(string symbol);

    void ReplaceHistory(string symbol, IEnumerable<PriceBar> bars);

    void UpsertBar(PriceBar bar);
}

public sealed class PriceHistoryStore : IPriceHistoryStore
{
    private readonly ConcurrentDictionary<string, List<PriceBar>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _capacity;

    public PriceHistoryStore(int capacity = 720)
    {
        _capacity = Math.Max(capacity, 1);
    }

    public IReadOnlyList<PriceBar> GetHistory(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (!_history.TryGetValue(symbol, out var list))
        {
            return Array.Empty<PriceBar>();
        }

        lock (list)
        {
            return list.ToArray();
        }
    }

    public void ReplaceHistory(string symbol, IEnumerable<PriceBar> bars)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(bars);

        var ordered = bars
            .Where(b => b.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.StartTimeUtc)
            .ToList();

        if (ordered.Count > _capacity)
        {
            ordered = ordered.Skip(ordered.Count - _capacity).ToList();
        }

        var list = _history.GetOrAdd(symbol, _ => new List<PriceBar>(_capacity));
        lock (list)
        {
            list.Clear();
            list.AddRange(ordered);
        }
    }

    public void UpsertBar(PriceBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        var list = _history.GetOrAdd(bar.Symbol, _ => new List<PriceBar>(_capacity));
        lock (list)
        {
            var index = list.FindIndex(existing => existing.StartTimeUtc == bar.StartTimeUtc);
            if (index >= 0)
            {
                list[index] = bar;
            }
            else
            {
                list.Add(bar);
                list.Sort(static (a, b) => a.StartTimeUtc.CompareTo(b.StartTimeUtc));
            }

            if (list.Count > _capacity)
            {
                var excess = list.Count - _capacity;
                list.RemoveRange(0, excess);
            }
        }
    }
}

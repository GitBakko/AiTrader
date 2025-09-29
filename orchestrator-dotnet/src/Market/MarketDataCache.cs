using System.Collections.Concurrent;

namespace Orchestrator.Market;

public interface IMarketDataCache
{
    void UpdateQuote(Quote quote);
    Quote? GetQuote(string symbol);
}

public sealed class MarketDataCache : IMarketDataCache
{
    private readonly ConcurrentDictionary<string, Quote> _quotes = new(StringComparer.OrdinalIgnoreCase);

    public void UpdateQuote(Quote quote)
    {
        _quotes[quote.Symbol] = quote;
    }

    public Quote? GetQuote(string symbol)
    {
        return _quotes.TryGetValue(symbol, out var quote) ? quote : null;
    }
}

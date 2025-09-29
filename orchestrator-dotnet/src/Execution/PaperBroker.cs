using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Contracts;
using Orchestrator.Application.Options;
using Orchestrator.Market;

namespace Orchestrator.Execution;

public sealed class PaperBroker : IBrokerAdapter
{
    private readonly IMarketDataCache _marketData;
    private readonly SlippageOptions _slippage;
    private readonly ILogger<PaperBroker> _logger;

    public PaperBroker(IMarketDataCache marketData, IOptionsMonitor<TradingOptions> options, ILogger<PaperBroker> logger)
    {
        _marketData = marketData;
        _slippage = options.CurrentValue.SlippageModel;
        _logger = logger;
    }

    public Task<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Paper cancel: {OrderId}", orderId);
        return Task.FromResult(true);
    }

    public Task<ExecutionResult> PlaceOrderAsync(TradeIntent intent, CancellationToken cancellationToken = default)
    {
        var quote = _marketData.GetQuote(intent.Instrument);
        if (quote is null)
        {
            throw new InvalidOperationException($"No market data available for {intent.Instrument}");
        }

        var fillPrice = CalculateFillPrice(intent, quote);
        var tradeId = Guid.NewGuid().ToString();
        var orderId = Guid.NewGuid().ToString();
        var fill = new Fill(tradeId, intent.Instrument, intent.Side, fillPrice, intent.IntendedQuantity, DateTime.UtcNow);

        _logger.LogInformation("Paper fill for {Instrument} {Side} qty {Qty} at {Price}", intent.Instrument, intent.Side, intent.IntendedQuantity, fillPrice);

        var metadata = new Dictionary<string, object>
        {
            ["slippageFraction"] = _slippage.Fraction,
            ["quoteBid"] = quote.Bid,
            ["quoteAsk"] = quote.Ask
        };

        return Task.FromResult(new ExecutionResult(orderId, fill, metadata));
    }

    private decimal CalculateFillPrice(TradeIntent intent, Quote quote)
    {
        var spread = Math.Max(quote.Ask - quote.Bid, 0m);
        var slip = spread * _slippage.Fraction;

        return intent.Side switch
        {
            TradeSide.Buy => quote.Ask + slip,
            TradeSide.Sell => quote.Bid - slip,
            _ => quote.Ask
        };
    }
}

using System;
using System.Collections.Generic;

namespace Orchestrator.Application.Contracts;

public interface IIndicatorCalculator
{
    void Update(MarketSample sample);

    IndicatorSnapshot Current { get; }
}

public sealed record MarketSample(
    string Symbol,
    DateTime TimestampUtc,
    decimal Price,
    decimal Volume,
    decimal Bid,
    decimal Ask);

public sealed record IndicatorSnapshot(
    DateTime TimestampUtc,
    decimal Vwap,
    decimal Ema200,
    decimal Sma20,
    decimal Atr14,
    decimal Rsi7,
    decimal Rsi14,
    decimal Sigma,
    decimal SpreadMedian,
    bool TrendOk,
    bool VolatilityOk,
    bool SpreadOk,
    IReadOnlyDictionary<string, decimal> Features);
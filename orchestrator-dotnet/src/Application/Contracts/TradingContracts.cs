using System;
using System.Collections.Generic;

namespace Orchestrator.Application.Contracts;

public enum TradeSide
{
    Buy,
    Sell
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit
}

public record TradeIntent(
    string Instrument,
    TradeSide Side,
    decimal IntendedQuantity,
    decimal EntryPrice,
    decimal StopPrice,
    string Strategy,
    decimal? TakeProfitPrice = null,
    OrderType OrderType = OrderType.Market,
    string? CorrelationId = null);

public record RiskDecision(bool Allowed, string Reason, decimal AllowedQuantity, decimal RiskFractionUsed);

public record AccountSnapshot(
    decimal Equity,
    decimal DailyPnlPct,
    decimal WeeklyPnlPct,
    int OpenPositions);

public record MarketSnapshot(
    decimal Atr,
    decimal Spread,
    bool TrendOk,
    bool MarketOpen);

public record Fill(
    string TradeId,
    string Instrument,
    TradeSide Side,
    decimal Price,
    decimal Quantity,
    DateTime TimestampUtc);

public record StrategySignal(
    string Instrument,
    string Strategy,
    TradeSide Side,
    decimal EntryPrice,
    decimal StopPrice,
    decimal? TargetPrice,
    decimal Score,
    decimal RiskFraction,
    IReadOnlyDictionary<string, decimal> Features,
    DateTime TimestampUtc);

public record ExecutionResult(
    string OrderId,
    Fill Fill,
    IDictionary<string, object>? Metadata = null);

using System.Collections.Generic;
using Orchestrator.Application.Contracts;
using Orchestrator.Infra;

namespace Orchestrator.Market;

public record Quote(string Symbol, decimal Bid, decimal Ask, DateTime TimestampUtc);

public record Trade(string Symbol, decimal Price, decimal Quantity, DateTime TimestampUtc);

public record BookTickerEvent(DateTime TimestampUtc, string Symbol, decimal Bid, decimal Ask) : IEvent;

public record TradeEvent(DateTime TimestampUtc, string Symbol, decimal Price, decimal Quantity) : IEvent;

public record KlineEvent(DateTime TimestampUtc, string Symbol, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, DateTime StartTimeUtc, DateTime CloseTimeUtc, bool IsFinal) : IEvent;

public record PriceBar(string Symbol, string Interval, DateTime StartTimeUtc, DateTime CloseTimeUtc, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

public record PriceHistorySnapshotEvent(DateTime TimestampUtc, string Symbol, string Interval, IReadOnlyList<PriceBar> Points) : IEvent;

public record CommoditySnapshotEvent(DateTime TimestampUtc, string Symbol, decimal Price, string Source) : IEvent;

public record StrategySignalEvent(DateTime TimestampUtc, StrategySignal Signal) : IEvent;

public record ExecutionFillEvent(DateTime TimestampUtc, Fill Fill, string OrderId, string Strategy) : IEvent;

public record AlertEvent(DateTime TimestampUtc, string Type, string Severity, string Detail, IReadOnlyDictionary<string, object>? Context) : IEvent;

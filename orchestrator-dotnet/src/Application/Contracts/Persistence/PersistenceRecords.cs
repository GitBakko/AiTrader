using System;
using System.Collections.Generic;
using Orchestrator.Application.Contracts;

namespace Orchestrator.Application.Contracts.Persistence;

public sealed record InstrumentRecord(
    string Symbol,
    string AssetClass,
    string Venue,
    decimal PointValue,
    decimal TickSize,
    string Currency,
    decimal LotSize,
    string? Tags);

public sealed record SignalRecord(
    DateTime TimestampUtc,
    string Symbol,
    string Strategy,
    TradeSide Side,
    decimal Score,
    decimal RiskFraction,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    DateTime? ExpiryUtc,
    IReadOnlyDictionary<string, decimal> Features,
    IReadOnlyDictionary<string, object>? Recommended,
    IReadOnlyDictionary<string, object>? Metadata);

public sealed record TradeRecord(
    string TradeIdentifier,
    string Symbol,
    string Strategy,
    TradeSide Side,
    decimal Quantity,
    decimal EntryPrice,
    decimal StopPrice,
    decimal? TargetPrice,
    decimal RiskPct,
    DateTime OpenedAtUtc,
    string? OrderId,
    string? CorrelationId,
    string? Notes);

public sealed record ExecutionRecord(
    string TradeIdentifier,
    string Symbol,
    TradeSide Side,
    decimal Price,
    decimal Quantity,
    DateTime TimestampUtc,
    string? OrderId,
    string? Venue,
    string? Source,
    decimal? Fee,
    string? FeeCurrency,
    string? Status,
    string? Liquidity,
    IReadOnlyDictionary<string, object>? Metadata);

public sealed record RiskEventRecord(
    DateTime TimestampUtc,
    string Type,
    string Severity,
    string? Symbol,
    string? TradeIdentifier,
    string? CorrelationId,
    string Detail,
    string? Data);

public sealed record AuditLogRecord(
    DateTime TimestampUtc,
    string Component,
    string Level,
    string Message,
    string? Detail,
    string? CorrelationId,
    string? Symbol,
    string? TradeIdentifier,
    IReadOnlyDictionary<string, object>? Metadata);

public sealed record PortfolioSnapshotRecord(
    DateTime TimestampUtc,
    decimal Equity,
    decimal Cash,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    decimal? GrossExposure,
    decimal? NetExposure,
    string? PositionsJson);

public sealed record RiskLimitRecord(
    string Mode,
    decimal PerTradePct,
    decimal DailyStopPct,
    decimal WeeklyStopPct,
    int MaxPositions,
    decimal? MaxGrossExposure,
    int CoolingMinutes,
    bool IsActive);

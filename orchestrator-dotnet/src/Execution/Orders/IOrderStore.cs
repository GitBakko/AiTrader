using System;
using System.Collections.Generic;
using Orchestrator.Application.Contracts;

namespace Orchestrator.Execution.Orders;

public enum OrderState
{
    New,
    Partial,
    Filled,
    Cancelled,
    Rejected
}

public sealed record OrderRecord(
    string OrderId,
    string Instrument,
    TradeSide Side,
    decimal RequestedQuantity,
    decimal FilledQuantity,
    decimal? AveragePrice,
    OrderState Status,
    string Source,
    string? Strategy,
    decimal EntryPrice,
    decimal StopPrice,
    decimal? TargetPrice,
    OrderType OrderType,
    string? CorrelationId,
    decimal RiskFraction,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyDictionary<string, object>? Metadata);

public interface IOrderStore
{
    OrderRecord RecordExecution(string source, TradeIntent intent, ExecutionResult execution, string? strategy);

    OrderRecord? GetById(string orderId);
}

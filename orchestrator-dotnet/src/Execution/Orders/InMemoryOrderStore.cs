using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Orchestrator.Application.Contracts;

namespace Orchestrator.Execution.Orders;

public sealed class InMemoryOrderStore : IOrderStore
{
    private readonly ConcurrentDictionary<string, OrderRecord> _orders = new();

    public OrderRecord RecordExecution(string source, TradeIntent intent, ExecutionResult execution, string? strategy)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(intent);

        var metadata = execution.Metadata is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(execution.Metadata);

        static decimal ExtractRiskFraction(IDictionary<string, object> source)
        {
            if (source.TryGetValue("riskFraction", out var value))
            {
                return value switch
                {
                    decimal dec => dec,
                    double dbl => (decimal)dbl,
                    float fl => (decimal)fl,
                    int i => i,
                    long l => l,
                    string s when decimal.TryParse(s, out var parsed) => parsed,
                    _ => 0m
                };
            }

            return 0m;
        }

        var riskFraction = ExtractRiskFraction(metadata);

        var record = new OrderRecord(
            execution.OrderId,
            intent.Instrument,
            intent.Side,
            intent.IntendedQuantity,
            execution.Fill.Quantity,
            execution.Fill.Price,
            OrderState.Filled,
            source,
            strategy,
            intent.EntryPrice,
            intent.StopPrice,
            intent.TakeProfitPrice,
            intent.OrderType,
            intent.CorrelationId,
            riskFraction,
            execution.Fill.TimestampUtc,
            execution.Fill.TimestampUtc,
            metadata);

        _orders[execution.OrderId] = record;
        return record;
    }

    public OrderRecord? GetById(string orderId)
    {
        return orderId is not null && _orders.TryGetValue(orderId, out var record)
            ? record
            : null;
    }
}

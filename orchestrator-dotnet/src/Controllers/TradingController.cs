using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Contracts;
using Orchestrator.Application.Options;
using Orchestrator.Execution.Orders;
using Orchestrator.Infra;
using Orchestrator.Market;

namespace Orchestrator.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class TradingController : ControllerBase
{
    private readonly IRiskManager _riskManager;
    private readonly IBrokerAdapter _broker;
    private readonly IOptionsMonitor<TradingOptions> _tradingOptions;
    private readonly IPortfolioService _portfolio;
    private readonly IIndicatorSnapshotStore _indicatorSnapshots;
    private readonly IMarketDataCache _marketData;
    private readonly IOrderStore _orderStore;
    private readonly IEventBus _eventBus;
    private readonly ILogger<TradingController> _logger;

    public TradingController(
        IRiskManager riskManager,
        IBrokerAdapter broker,
        IOptionsMonitor<TradingOptions> tradingOptions,
        IPortfolioService portfolio,
        IIndicatorSnapshotStore indicatorSnapshots,
        IMarketDataCache marketData,
        IOrderStore orderStore,
        IEventBus eventBus,
        ILogger<TradingController> logger)
    {
        _riskManager = riskManager;
        _broker = broker;
        _tradingOptions = tradingOptions;
        _portfolio = portfolio;
        _indicatorSnapshots = indicatorSnapshots;
        _marketData = marketData;
        _orderStore = orderStore;
        _eventBus = eventBus;
        _logger = logger;
    }

    [HttpGet("limits")]
    public ActionResult<RiskLimitResponse> GetLimits()
    {
        var risk = _tradingOptions.CurrentValue.Risk;
        var trading = _tradingOptions.CurrentValue;
        return Ok(new RiskLimitResponse(risk.PerTradePct, risk.DailyStopPct, risk.WeeklyStopPct, risk.MaxPositions, trading.RejectRatePct));
    }

    [HttpPost("trade/intents")]
    public async Task<ActionResult<RiskDecisionResponse>> CheckTradeIntent([FromBody] TradeIntentRequest request, CancellationToken cancellationToken)
    {
        if (!TryParseSide(request.Side, out var side))
        {
            return BadRequest("Invalid side");
        }

        var intent = new TradeIntent(
            request.Instrument,
            side,
            request.IntendedQty,
            request.Entry,
            request.Stop,
            request.Strategy,
            request.TakeProfit,
            OrderType.Market,
            request.CorrelationId ?? Guid.NewGuid().ToString());

        var account = _portfolio.GetSnapshot();
        var market = BuildMarketSnapshot(intent.Instrument);
        var decision = await _riskManager.PreTradeCheckAsync(intent, account, market, cancellationToken).ConfigureAwait(false);

        return Ok(new RiskDecisionResponse(decision.Allowed, decision.Reason, decision.AllowedQuantity, decision.RiskFractionUsed));
    }

    [HttpPost("orders")]
    public async Task<IActionResult> SubmitOrder([FromBody] OrderCommandRequest request, CancellationToken cancellationToken)
    {
        if (!TryParseSide(request.Side, out var side))
        {
            return BadRequest("Invalid side");
        }

        if (!TryParseOrderType(request.OrderType, out var orderType))
        {
            return BadRequest("Invalid order_type");
        }

        var quote = _marketData.GetQuote(request.Instrument);
        if (!TryResolveEntryPrice(orderType, side, request, quote, out var entryPrice, out var error))
        {
            return BadRequest(error);
        }

        if (!TryResolveStopPrice(request, out var stopPrice, out var stopError))
        {
            return BadRequest(stopError);
        }

        var takeProfit = request.Bracket?.TakeProfit;

        var intent = new TradeIntent(
            request.Instrument,
            side,
            request.Quantity,
            entryPrice,
            stopPrice,
            request.Strategy ?? "MANUAL",
            takeProfit,
            orderType,
            request.CorrelationId ?? Guid.NewGuid().ToString());

        var account = _portfolio.GetSnapshot();
        var market = BuildMarketSnapshot(intent.Instrument);
        var decision = await _riskManager.PreTradeCheckAsync(intent, account, market, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed || decision.AllowedQuantity <= 0m)
        {
            _logger.LogInformation("Manual order rejected for {Instrument}: {Reason}", intent.Instrument, decision.Reason);
            await _eventBus.PublishAsync(new AlertEvent(DateTime.UtcNow, "BROKER_REJECT", "WARN", decision.Reason, new Dictionary<string, object>
            {
                ["instrument"] = intent.Instrument,
                ["strategy"] = intent.Strategy,
                ["side"] = side.ToString().ToUpperInvariant()
            }), cancellationToken).ConfigureAwait(false);
            return Problem(detail: decision.Reason, statusCode: StatusCodes.Status409Conflict, title: "RiskRejected");
        }

        intent = intent with { IntendedQuantity = Math.Min(intent.IntendedQuantity, decision.AllowedQuantity) };

        ExecutionResult execution;
        try
        {
            execution = await _broker.PlaceOrderAsync(intent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order placement failed for {Instrument}", intent.Instrument);
            await _eventBus.PublishAsync(new AlertEvent(DateTime.UtcNow, "BROKER_REJECT", "CRIT", ex.Message, new Dictionary<string, object>
            {
                ["instrument"] = intent.Instrument,
                ["strategy"] = intent.Strategy,
                ["side"] = side.ToString().ToUpperInvariant()
            }), cancellationToken).ConfigureAwait(false);
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "OrderPlacementFailed");
        }

        await _riskManager.PostTradeUpdateAsync(execution.Fill, market, cancellationToken).ConfigureAwait(false);
        _portfolio.ApplyFill(execution.Fill);
        var record = _orderStore.RecordExecution("manual", intent, execution, intent.Strategy);
        await _eventBus.PublishAsync(new ExecutionFillEvent(execution.Fill.TimestampUtc, execution.Fill, execution.OrderId, intent.Strategy), cancellationToken).ConfigureAwait(false);

        return Accepted(new { orderId = record.OrderId });
    }

    [HttpGet("orders/{id}")]
    public ActionResult<OrderStatusResponse> GetOrder(string id)
    {
        var record = _orderStore.GetById(id);
        if (record is null)
        {
            return NotFound();
        }

        return Ok(new OrderStatusResponse(
            record.OrderId,
            record.Instrument,
            record.Side.ToString().ToUpperInvariant(),
            record.RequestedQuantity,
            record.FilledQuantity,
            record.AveragePrice,
            MapOrderState(record.Status),
            record.Source,
            record.Strategy,
            record.CreatedAtUtc,
            record.Metadata));
    }

    private MarketSnapshot BuildMarketSnapshot(string instrument)
    {
        var snapshot = _indicatorSnapshots.GetLatest(instrument);
        if (snapshot is null)
        {
            var quote = _marketData.GetQuote(instrument);
            var spread = quote is null ? 0m : Math.Max(0m, quote.Ask - quote.Bid);
            return new MarketSnapshot(0m, spread, true, true);
        }

        var spreadValue = snapshot.Features.TryGetValue("spread", out var spreadFeature) ? spreadFeature : snapshot.SpreadMedian;
        var marketOpen = snapshot.VolatilityOk && snapshot.SpreadOk;
        return new MarketSnapshot(snapshot.Atr14, spreadValue, snapshot.TrendOk, marketOpen);
    }

    private static bool TryParseSide(string? raw, out TradeSide side)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            side = TradeSide.Buy;
            return false;
        }

        if (Enum.TryParse<TradeSide>(raw, true, out side))
        {
            return true;
        }

        side = TradeSide.Buy;
        return false;
    }

    private static bool TryParseOrderType(string? raw, out OrderType orderType)
    {
        switch (raw?.ToUpperInvariant())
        {
            case "MKT":
                orderType = OrderType.Market;
                return true;
            case "LMT":
                orderType = OrderType.Limit;
                return true;
            case "STP":
                orderType = OrderType.Stop;
                return true;
            case "STP_LMT":
                orderType = OrderType.StopLimit;
                return true;
            default:
                orderType = OrderType.Market;
                return false;
        }
    }

    private static bool TryResolveEntryPrice(OrderType orderType, TradeSide side, OrderCommandRequest request, Quote? quote, out decimal entry, out string? error)
    {
        entry = 0m;
        error = null;

        switch (orderType)
        {
            case OrderType.Market:
                if (quote is null)
                {
                    if (request.LimitPrice is null)
                    {
                        error = "Market order requires live quote or limit_price fallback";
                        return false;
                    }

                    entry = request.LimitPrice.Value;
                    return true;
                }

                entry = side == TradeSide.Buy ? quote.Ask : quote.Bid;
                return true;
            case OrderType.Limit:
                if (request.LimitPrice is null)
                {
                    error = "limit_price required for LMT";
                    return false;
                }

                entry = request.LimitPrice.Value;
                return true;
            case OrderType.Stop:
                if (request.StopPrice is null)
                {
                    error = "stop_price required for STP";
                    return false;
                }

                entry = request.StopPrice.Value;
                return true;
            case OrderType.StopLimit:
                if (request.LimitPrice is null || request.StopPrice is null)
                {
                    error = "stop_price and limit_price required for STP_LMT";
                    return false;
                }

                entry = request.LimitPrice.Value;
                return true;
            default:
                error = "Unsupported order type";
                return false;
        }
    }

    private static bool TryResolveStopPrice(OrderCommandRequest request, out decimal stop, out string? error)
    {
        stop = 0m;
        error = null;

        var userStop = request.StopPrice ?? request.Bracket?.StopLoss;
        if (userStop is null)
        {
            error = "stop_price or bracket.stop_loss required";
            return false;
        }

        stop = userStop.Value;
        return true;
    }

    private static string MapOrderState(OrderState state)
    {
        return state switch
        {
            OrderState.New => "NEW",
            OrderState.Partial => "PARTIAL",
            OrderState.Filled => "FILLED",
            OrderState.Cancelled => "CANCELLED",
            OrderState.Rejected => "REJECTED",
            _ => "UNKNOWN"
        };
    }

    public sealed record RiskLimitResponse(
        [property: JsonPropertyName("per_trade_pct")] decimal PerTradePct,
        [property: JsonPropertyName("daily_stop_pct")] decimal DailyStopPct,
        [property: JsonPropertyName("weekly_stop_pct")] decimal WeeklyStopPct,
        [property: JsonPropertyName("max_positions")] int MaxPositions,
        [property: JsonPropertyName("reject_rate_pct")] decimal RejectRatePct);

    public sealed record TradeIntentRequest(
        [property: JsonPropertyName("instrument")] string Instrument,
        [property: JsonPropertyName("side")] string Side,
        [property: JsonPropertyName("intendedQty")] decimal IntendedQty,
        [property: JsonPropertyName("entry")] decimal Entry,
        [property: JsonPropertyName("stop")] decimal Stop,
        [property: JsonPropertyName("strategy")] string Strategy,
        [property: JsonPropertyName("takeProfit")] decimal? TakeProfit,
        [property: JsonPropertyName("correlationId")] string? CorrelationId);

    public sealed record RiskDecisionResponse(
        [property: JsonPropertyName("allowed")] bool Allowed,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("allowedQty")] decimal AllowedQty,
        [property: JsonPropertyName("riskFraction")] decimal RiskFraction);

    public sealed record OrderCommandRequest(
        [property: JsonPropertyName("instrument")] string Instrument,
        [property: JsonPropertyName("side")] string Side,
        [property: JsonPropertyName("qty")] decimal Quantity,
        [property: JsonPropertyName("order_type")] string OrderType,
        [property: JsonPropertyName("limit_price")] decimal? LimitPrice,
        [property: JsonPropertyName("stop_price")] decimal? StopPrice,
        [property: JsonPropertyName("tif")] string? TimeInForce,
        [property: JsonPropertyName("bracket")] BracketRequest? Bracket,
        [property: JsonPropertyName("strategy")] string? Strategy,
        [property: JsonPropertyName("correlationId")] string? CorrelationId);

    public sealed record BracketRequest(
        [property: JsonPropertyName("take_profit")] decimal? TakeProfit,
        [property: JsonPropertyName("stop_loss")] decimal? StopLoss);

    public sealed record OrderStatusResponse(
        [property: JsonPropertyName("order_id")] string OrderId,
        [property: JsonPropertyName("instrument")] string Instrument,
        [property: JsonPropertyName("side")] string Side,
        [property: JsonPropertyName("qty")] decimal RequestedQuantity,
        [property: JsonPropertyName("filled_qty")] decimal FilledQuantity,
        [property: JsonPropertyName("avg_price")] decimal? AveragePrice,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("strategy")] string? Strategy,
        [property: JsonPropertyName("ts")] DateTime TimestampUtc,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, object>? Metadata);
}

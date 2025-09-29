using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Contracts;
using Orchestrator.Application.Options;

namespace Orchestrator.Execution.Risk;

public sealed class DefaultRiskManager : IRiskManager
{
    private readonly ILogger<DefaultRiskManager> _logger;
    private readonly IOptionsMonitor<TradingOptions> _options;

    public DefaultRiskManager(ILogger<DefaultRiskManager> logger, IOptionsMonitor<TradingOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public Task PostTradeUpdateAsync(Fill fill, MarketSnapshot market, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Risk update: trade {TradeId} {Instrument} fill {Side} qty {Qty} at {Price}", fill.TradeId, fill.Instrument, fill.Side, fill.Quantity, fill.Price);
        return Task.CompletedTask;
    }

    public Task<RiskDecision> PreTradeCheckAsync(TradeIntent intent, AccountSnapshot account, MarketSnapshot market, CancellationToken cancellationToken = default)
    {
        var riskOptions = _options.CurrentValue.Risk;

        if (account.DailyPnlPct <= -riskOptions.DailyStopPct)
        {
            return Task.FromResult(new RiskDecision(false, "Daily stop reached", 0m, riskOptions.DailyStopPct));
        }

        if (account.WeeklyPnlPct <= -riskOptions.WeeklyStopPct)
        {
            return Task.FromResult(new RiskDecision(false, "Weekly stop reached", 0m, riskOptions.WeeklyStopPct));
        }

        if (account.OpenPositions >= riskOptions.MaxPositions)
        {
            return Task.FromResult(new RiskDecision(false, "Max concurrent positions reached", 0m, 0m));
        }

        if (!market.TrendOk)
        {
            return Task.FromResult(new RiskDecision(false, "Trend filter disallows trade", 0m, 0m));
        }

        var riskCapital = account.Equity * riskOptions.PerTradePct;
        var riskPerUnit = Math.Abs(intent.EntryPrice - intent.StopPrice);
        if (riskPerUnit <= 0)
        {
            return Task.FromResult(new RiskDecision(false, "Invalid stop distance", 0m, 0m));
        }

        var allowedQty = Math.Floor(riskCapital / riskPerUnit);
        if (allowedQty <= 0)
        {
            return Task.FromResult(new RiskDecision(false, "Position size below 1 contract", 0m, riskOptions.PerTradePct));
        }

        _logger.LogInformation("Risk check passed for {Instrument} {Side} qty {Qty}", intent.Instrument, intent.Side, allowedQty);
        return Task.FromResult(new RiskDecision(true, "OK", allowedQty, riskOptions.PerTradePct));
    }
}

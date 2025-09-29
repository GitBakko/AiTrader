using Microsoft.Extensions.Options;
using Orchestrator.Application.Configuration;

namespace Orchestrator.Application.Options;

public sealed class FreeModeTradingOptionsConfigurator : IConfigureOptions<TradingOptions>
{
    private readonly IFreeModeConfigProvider _provider;

    public FreeModeTradingOptionsConfigurator(IFreeModeConfigProvider provider)
    {
        _provider = provider;
    }

    public void Configure(TradingOptions options)
    {
        var config = _provider.Current;
        options.Risk.PerTradePct = config.Risk.PerTradePct;
        options.Risk.DailyStopPct = config.Risk.DailyStopPct;
        options.Risk.WeeklyStopPct = config.Risk.WeeklyStopPct;
        options.SlippageModel.Fraction = config.Execution.SlippageModel.Fraction;
        options.RejectRatePct = config.Execution.RejectRatePct;
    }
}

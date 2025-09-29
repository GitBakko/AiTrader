namespace Orchestrator.Application.Options;

public sealed class TradingOptions
{
    public RiskOptions Risk { get; set; } = new();

    public SlippageOptions SlippageModel { get; set; } = new();

    public decimal RejectRatePct { get; set; } = 0.5m;
}

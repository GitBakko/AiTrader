namespace Orchestrator.Application.Options;

public sealed class RiskOptions
{
    public decimal PerTradePct { get; set; } = 0.0035m;
    public decimal DailyStopPct { get; set; } = 0.02m;
    public decimal WeeklyStopPct { get; set; } = 0.04m;
    public int MaxPositions { get; set; } = 3;
}

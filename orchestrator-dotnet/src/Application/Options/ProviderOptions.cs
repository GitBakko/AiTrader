namespace Orchestrator.Application.Options;

public sealed class ProviderOptions
{
    public string FreeModeConfigPath { get; set; } = "../../configs/free_mode.yaml";

    public string AlphaVantageLimiterStatePath { get; set; } = "../../state/alphavantage_limiter.json";

    public int AlphaVantageDefaultQuota { get; set; } = 25;
}

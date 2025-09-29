using System.Collections.Generic;

namespace Orchestrator.Application.Configuration;

public sealed class FreeModeConfig
{
    public string Mode { get; set; } = "FREE";
    public string Timezone { get; set; } = "Europe/Rome";
    public RiskSection Risk { get; set; } = new();
    public ExecutionSection Execution { get; set; } = new();
    public Dictionary<string, StrategySection> Strategies { get; set; } = new();
    public CryptoSection? Crypto { get; set; } = new();
    public StocksSection? Stocks { get; set; } = new();
    public ForexSection? Forex { get; set; } = new();
    public CommoditiesSection? Commodities { get; set; } = new();

    public sealed class RiskSection
    {
        public decimal PerTradePct { get; set; } = 0.0035m;
        public decimal DailyStopPct { get; set; } = 0.02m;
        public decimal WeeklyStopPct { get; set; } = 0.04m;
    }

    public sealed class ExecutionSection
    {
        public string Broker { get; set; } = "paper";
        public SlippageSection SlippageModel { get; set; } = new();
        public decimal RejectRatePct { get; set; } = 0.5m;

        public sealed class SlippageSection
        {
            public string Type { get; set; } = "spread_fraction";
            public decimal Fraction { get; set; } = 0.25m;
        }
    }

    public sealed class StrategySection
    {
        public bool Enabled { get; set; } = true;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public sealed class CryptoSection
    {
        public string Provider { get; set; } = "binance";
        public string WsBase { get; set; } = "wss://stream.binance.com:9443";
        public List<string> Symbols { get; set; } = new();
        public List<string> Streams { get; set; } = new();
        public string KlinesInterval { get; set; } = "1m";
    }

    public sealed class StocksSection
    {
        public string Provider { get; set; } = "finnhub";
        public string WsUrl { get; set; } = string.Empty;
        public List<string> Symbols { get; set; } = new();
        public List<string> Channels { get; set; } = new();
    }

    public sealed class ForexSection
    {
        public string Provider { get; set; } = "finnhub";
        public string WsUrl { get; set; } = string.Empty;
        public List<string> Pairs { get; set; } = new();
        public List<string> Channels { get; set; } = new();
    }

    public sealed class CommoditiesSection
    {
        public string Provider { get; set; } = "alphavantage";
        public string BaseUrl { get; set; } = "https://www.alphavantage.co/query";
        public string ApiKeyEnv { get; set; } = "ALPHAVANTAGE_API_KEY";
        public CommoditySymbols Symbols { get; set; } = new();
        public PollingPolicySection PollingPolicy { get; set; } = new();

        public sealed class CommoditySymbols
        {
            public List<string> Energy { get; set; } = new();
            public List<string> Metals { get; set; } = new();
            public List<string> MetalsFx { get; set; } = new();
        }

        public sealed class PollingPolicySection
        {
            public bool TimezoneResetUtc { get; set; } = true;
            public int DailyQuota { get; set; } = 25;
            public int DefaultIntervalMinutes { get; set; } = 360;
            public BackoffSection Backoff { get; set; } = new();
        }

        public sealed class BackoffSection
        {
            public int BaseSeconds { get; set; } = 10;
            public int MaxSeconds { get; set; } = 600;
        }
    }
}

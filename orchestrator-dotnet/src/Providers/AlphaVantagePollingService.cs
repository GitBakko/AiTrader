using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Application.Configuration;
using Orchestrator.Application.Options;
using Orchestrator.Infra;
using Orchestrator.Market;

namespace Orchestrator.Providers;

internal sealed class AlphaVantagePollingService : BackgroundService
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(15);

    private readonly ILogger<AlphaVantagePollingService> _logger;
    private readonly IEventBus _eventBus;
    private readonly IFreeModeConfigProvider _configProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _statePath;
    private readonly int _defaultQuota;

    public AlphaVantagePollingService(
        ILogger<AlphaVantagePollingService> logger,
        IEventBus eventBus,
        IFreeModeConfigProvider configProvider,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<ProviderOptions> providerOptions)
    {
        _logger = logger;
        _eventBus = eventBus;
        _configProvider = configProvider;
        _httpClientFactory = httpClientFactory;

        var opts = providerOptions.CurrentValue;
        _statePath = ResolvePath(opts.AlphaVantageLimiterStatePath);
        _defaultQuota = Math.Max(1, opts.AlphaVantageDefaultQuota);
        EnsureDirectory(_statePath);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alpha Vantage polling error");
            }

            var interval = GetInterval();
            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private TimeSpan GetInterval()
    {
        var config = _configProvider.Current;
        var policy = config.Commodities?.PollingPolicy;
        if (policy is null)
        {
            return MinInterval;
        }

        var minutes = Math.Clamp(policy.DefaultIntervalMinutes, (int)MinInterval.TotalMinutes, (int)MaxInterval.TotalMinutes);
        return TimeSpan.FromMinutes(minutes);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var config = _configProvider.Current;
        var commodities = config.Commodities;
        if (commodities is null || !"alphavantage".Equals(commodities.Provider, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Alpha Vantage provider disabled; skip poll");
            return;
        }

        var apiKeyEnv = string.IsNullOrWhiteSpace(commodities.ApiKeyEnv) ? "ALPHAVANTAGE_API_KEY" : commodities.ApiKeyEnv;
        var apiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Alpha Vantage API key missing (env {Env}); skipping poll", apiKeyEnv);
            return;
        }

        var baseUrl = ProviderUtils.ExpandPlaceholders(commodities.BaseUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://www.alphavantage.co/query";
        }

        var quota = Math.Max(1, commodities.PollingPolicy?.DailyQuota ?? _defaultQuota);
        var limiter = new AlphaVantageRateLimiter(_statePath, quota);
        var httpClient = _httpClientFactory.CreateClient("AlphaVantage");
        var client = new AlphaVantageClient(httpClient, baseUrl, apiKey, limiter);

        foreach (var symbol in commodities.Symbols.Energy)
        {
            ct.ThrowIfCancellationRequested();
            await FetchCommodityAsync(client, symbol, BuildFunctionQuery(symbol), ct).ConfigureAwait(false);
            await ThrottleAsync(ct).ConfigureAwait(false);
        }

        foreach (var symbol in commodities.Symbols.Metals)
        {
            ct.ThrowIfCancellationRequested();
            await FetchCommodityAsync(client, symbol, BuildFunctionQuery(symbol), ct).ConfigureAwait(false);
            await ThrottleAsync(ct).ConfigureAwait(false);
        }

        foreach (var fxSymbol in commodities.Symbols.MetalsFx)
        {
            var query = BuildFxQuery(fxSymbol);
            if (string.IsNullOrWhiteSpace(query))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            await FetchCommodityAsync(client, fxSymbol, query, ct).ConfigureAwait(false);
            await ThrottleAsync(ct).ConfigureAwait(false);
        }
    }

    private static string BuildFunctionQuery(string symbol)
    {
        var trimmed = symbol.Trim().ToUpperInvariant();
        return $"function={trimmed}";
    }

    private static string BuildFxQuery(string symbol)
    {
        var trimmed = symbol.Trim().ToUpperInvariant();
        if (trimmed.Length < 6)
        {
            return string.Empty;
        }

        var from = trimmed[..3];
        var to = trimmed[3..];
        return $"function=CURRENCY_EXCHANGE_RATE&from_currency={from}&to_currency={to}";
    }

    private async Task FetchCommodityAsync(AlphaVantageClient client, string symbol, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        try
        {
            using var document = await client.GetAsync(query).ConfigureAwait(false);
            if (document is null)
            {
                _logger.LogWarning("Alpha Vantage response null for {Symbol}", symbol);
                return;
            }

            if (TryExtractPrice(document.RootElement, symbol, out var price, out var timestamp))
            {
                await _eventBus.PublishAsync(new CommoditySnapshotEvent(timestamp, symbol, price, "AlphaVantage"), ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Alpha Vantage payload unrecognized for {Symbol}", symbol);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Alpha Vantage data for {Symbol}", symbol);
        }
    }

    private static bool TryExtractPrice(JsonElement root, string symbol, out decimal price, out DateTime timestampUtc)
    {
        price = 0m;
        timestampUtc = DateTime.UtcNow;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Note", out _))
        {
            return false;
        }

        if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array && dataProp.GetArrayLength() > 0)
        {
            var first = dataProp[0];
            if (TryParseDecimal(first, "value", out price))
            {
                timestampUtc = TryParseDate(first, "date") ?? DateTime.UtcNow;
                return true;
            }
        }

        if (root.TryGetProperty("Realtime Currency Exchange Rate", out var fxRoot))
        {
            if (fxRoot.TryGetProperty("5. Exchange Rate", out var rateProp))
            {
                price = rateProp.ValueKind == JsonValueKind.String && decimal.TryParse(rateProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
                timestampUtc = ParseTimestamp(fxRoot, "6. Last Refreshed") ?? DateTime.UtcNow;
                return price > 0m;
            }
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.StartsWith("Time Series", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var series = property.Value;
            foreach (var entry in series.EnumerateObject())
            {
                if (TryParseSeriesEntry(entry.Value, out price))
                {
                    timestampUtc = ParseDate(entry.Name) ?? DateTime.UtcNow;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseSeriesEntry(JsonElement element, out decimal price)
    {
        price = 0m;
        string[] candidates = { "4. close", "1. open", "5. Value" };
        foreach (var candidate in candidates)
        {
            if (element.TryGetProperty(candidate, out var prop) && prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out price))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDecimal(JsonElement element, string property, out decimal value)
    {
        value = 0m;
        if (!element.TryGetProperty(property, out var prop))
        {
            return false;
        }

        if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out value))
        {
            return true;
        }

        return false;
    }

    private static DateTime? TryParseDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
        {
            return null;
        }

        return ParseDate(prop.GetString());
    }

    private static DateTime? ParseTimestamp(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
        {
            return null;
        }

        return ParseDate(prop.GetString());
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedOffset))
        {
            return parsedOffset.UtcDateTime;
        }

        return null;
    }

    private static string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var basePath = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(basePath, configuredPath));
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static async Task ThrottleAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(RequestSpacing, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ignored
        }
    }
}

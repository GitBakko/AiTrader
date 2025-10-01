using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Application.Configuration;
using Orchestrator.Infra;
using Orchestrator.Market;

namespace Orchestrator.Realtime;

internal sealed class PriceHistoryProjection : IHostedService, IDisposable
{
    private const string DefaultInterval = "1m";
    private static readonly TimeSpan HistorySpan = TimeSpan.FromHours(6);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPriceHistoryStore _historyStore;
    private readonly IEventBus _eventBus;
    private readonly IFreeModeConfigProvider _configProvider;
    private readonly ILogger<PriceHistoryProjection> _logger;
    private readonly List<IDisposable> _subscriptions = new();

    private string _interval = DefaultInterval;

    public PriceHistoryProjection(
        IHttpClientFactory httpClientFactory,
        IPriceHistoryStore historyStore,
        IEventBus eventBus,
        IFreeModeConfigProvider configProvider,
        ILogger<PriceHistoryProjection> logger)
    {
        _httpClientFactory = httpClientFactory;
        _historyStore = historyStore;
        _eventBus = eventBus;
        _configProvider = configProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await BootstrapAsync(cancellationToken).ConfigureAwait(false);
        _subscriptions.Add(_eventBus.Subscribe<KlineEvent>(OnKlineAsync));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        var crypto = _configProvider.Current.Crypto;
        if (crypto is null || crypto.Symbols.Count == 0)
        {
            _logger.LogWarning("Price history bootstrap skipped: crypto feed disabled");
            return;
        }

        if (!string.Equals(crypto.Provider, "binance", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Price history bootstrap requires Binance provider; current provider {Provider}", crypto.Provider);
            return;
        }

        _interval = string.IsNullOrWhiteSpace(crypto.KlinesInterval) ? DefaultInterval : crypto.KlinesInterval;
        var limit = Math.Max((int)Math.Ceiling(HistorySpan.TotalMinutes), 1);

        foreach (var symbol in crypto.Symbols)
        {
            try
            {
                var bars = await FetchInitialBarsAsync(symbol, _interval, limit, cancellationToken).ConfigureAwait(false);
                if (bars.Count == 0)
                {
                    continue;
                }

                _historyStore.ReplaceHistory(symbol, bars);
                await PublishSnapshotAsync(symbol, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Bootstrapped price history for {Symbol} with {Count} bars", symbol, bars.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to bootstrap price history for {Symbol}", symbol);
            }
        }
    }

    private async ValueTask OnKlineAsync(KlineEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            var bar = new PriceBar(
                evt.Symbol,
                _interval,
                evt.StartTimeUtc,
                evt.CloseTimeUtc,
                evt.Open,
                evt.High,
                evt.Low,
                evt.Close,
                evt.Volume);

            _historyStore.UpsertBar(bar);

            if (evt.IsFinal)
            {
                await PublishSnapshotAsync(evt.Symbol, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process kline for {Symbol}", evt.Symbol);
        }
    }

    private async Task PublishSnapshotAsync(string symbol, CancellationToken cancellationToken)
    {
        var history = _historyStore.GetHistory(symbol);
        if (history.Count == 0)
        {
            return;
        }

        var snapshot = new PriceHistorySnapshotEvent(
            DateTime.UtcNow,
            symbol,
            _interval,
            history);

        await _eventBus.PublishAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PriceBar>> FetchInitialBarsAsync(string symbol, string interval, int limit, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("BinancePublic");
        var requestUri = $"/api/v3/klines?symbol={symbol.ToUpperInvariant()}&interval={interval}&limit={limit}";

        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PriceBar>();
        }

        var bars = new List<PriceBar>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 6)
            {
                continue;
            }

            var openTimeMs = element[0].GetInt64();
            var open = ParseDecimal(element[1]);
            var high = ParseDecimal(element[2]);
            var low = ParseDecimal(element[3]);
            var close = ParseDecimal(element[4]);
            var volume = ParseDecimal(element[5]);
            var closeTimeMs = element.GetArrayLength() > 6 ? element[6].GetInt64() : openTimeMs;

            bars.Add(new PriceBar(
                symbol,
                interval,
                DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime,
                DateTimeOffset.FromUnixTimeMilliseconds(closeTimeMs).UtcDateTime,
                open,
                high,
                low,
                close,
                volume));
        }

        return bars;
    }

    private static decimal ParseDecimal(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0m
        };
    }
}

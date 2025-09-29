using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Application.Configuration;
using Orchestrator.Infra;
using Orchestrator.Market;

namespace Orchestrator.Providers;

internal sealed class BinanceWebSocketService : BackgroundService
{
    private static readonly TimeSpan MinReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(2);

    private readonly ILogger<BinanceWebSocketService> _logger;
    private readonly IEventBus _eventBus;
    private readonly IFreeModeConfigProvider _configProvider;

    public BinanceWebSocketService(
        ILogger<BinanceWebSocketService> logger,
        IEventBus eventBus,
        IFreeModeConfigProvider configProvider)
    {
        _logger = logger;
        _eventBus = eventBus;
        _configProvider = configProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = MinReconnectDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cryptoConfig = _configProvider.Current.Crypto;
                if (cryptoConfig is null || !"binance".Equals(cryptoConfig.Provider, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Binance provider disabled via configuration; retrying in 10 minutes");
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var uri = BuildStreamUri(cryptoConfig);
                if (uri is null)
                {
                    _logger.LogWarning("No Binance streams configured; retrying in 5 minutes");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await ConnectAndStreamAsync(uri, stoppingToken).ConfigureAwait(false);
                delay = MinReconnectDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Binance socket loop error; reconnecting in {Delay}s", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxReconnectDelay.TotalSeconds));
            }
        }
    }

    private static Uri? BuildStreamUri(FreeModeConfig.CryptoSection crypto)
    {
        if (crypto.Symbols.Count == 0)
        {
            return null;
        }

        var streams = new List<string>();
        foreach (var symbol in crypto.Symbols)
        {
            var baseSymbol = symbol.ToLowerInvariant();
            foreach (var stream in crypto.Streams)
            {
                streams.Add($"{baseSymbol}@{stream}");
            }

            if (!string.IsNullOrWhiteSpace(crypto.KlinesInterval))
            {
                streams.Add($"{baseSymbol}@kline_{crypto.KlinesInterval}");
            }
        }

        if (streams.Count == 0)
        {
            return null;
        }

        var streamPath = string.Join('/', streams);
        var baseUrl = crypto.WsBase.TrimEnd('/');
        return new Uri($"{baseUrl}/stream?streams={streamPath}");
    }

    private async Task ConnectAndStreamAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions
        {
            ClientMaxWindowBits = 15,
            ServerMaxWindowBits = 15,
            ClientContextTakeover = false,
            ServerContextTakeover = false
        };

        _logger.LogInformation("Connecting to Binance WS {Uri}", uri);
        await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Connected to Binance WS {Uri}", uri);

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var messageStream = new MemoryStream();
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var segment = new ArraySegment<byte>(buffer);
                var result = await socket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogWarning("Binance WS requested close: {Status} - {Description}", result.CloseStatus, result.CloseStatusDescription);
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                messageStream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    var payload = Encoding.UTF8.GetString(messageStream.GetBuffer(), 0, (int)messageStream.Length);
                    await ProcessPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
                    messageStream.SetLength(0);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessPayloadAsync(string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("stream", out var streamProp) && root.TryGetProperty("data", out var dataProp))
            {
                var streamName = streamProp.GetString() ?? string.Empty;

                if (streamName.EndsWith("@bookTicker", StringComparison.Ordinal))
                {
                    await PublishBookTickerAsync(dataProp, cancellationToken).ConfigureAwait(false);
                }
                else if (streamName.EndsWith("@trade", StringComparison.Ordinal))
                {
                    await PublishTradeAsync(dataProp, cancellationToken).ConfigureAwait(false);
                }
                else if (streamName.Contains("@kline_", StringComparison.Ordinal))
                {
                    await PublishKlineAsync(dataProp, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (root.TryGetProperty("result", out _))
            {
                _logger.LogDebug("Received Binance subscription acknowledgement");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Binance payload: {Payload}", payload);
        }
    }

    private async ValueTask PublishBookTickerAsync(JsonElement data, CancellationToken ct)
    {
        var symbol = data.TryGetProperty("s", out var symbolProp) ? symbolProp.GetString() ?? string.Empty : string.Empty;
        var bid = data.TryGetProperty("b", out var bidProp) ? ParseDecimal(bidProp) : 0m;
        var ask = data.TryGetProperty("a", out var askProp) ? ParseDecimal(askProp) : 0m;
        var eventTime = data.TryGetProperty("E", out var eventProp)
            ? ParseUnixMilliseconds(eventProp)
            : DateTime.UtcNow;

        await _eventBus.PublishAsync(new BookTickerEvent(eventTime, symbol, bid, ask), ct).ConfigureAwait(false);
    }

    private async ValueTask PublishTradeAsync(JsonElement data, CancellationToken ct)
    {
        var symbol = data.GetProperty("s").GetString() ?? string.Empty;
        var price = ParseDecimal(data.GetProperty("p"));
        var quantity = ParseDecimal(data.GetProperty("q"));
        var tradeTime = ParseUnixMilliseconds(data.GetProperty("T"));

        await _eventBus.PublishAsync(new TradeEvent(tradeTime, symbol, price, quantity), ct).ConfigureAwait(false);
    }

    private async ValueTask PublishKlineAsync(JsonElement data, CancellationToken ct)
    {
        if (!data.TryGetProperty("k", out var kline))
        {
            return;
        }

        var symbol = kline.GetProperty("s").GetString() ?? string.Empty;
        var open = ParseDecimal(kline.GetProperty("o"));
        var high = ParseDecimal(kline.GetProperty("h"));
        var low = ParseDecimal(kline.GetProperty("l"));
        var close = ParseDecimal(kline.GetProperty("c"));
        var volume = ParseDecimal(kline.GetProperty("v"));
        var startTime = ParseUnixMilliseconds(kline.GetProperty("t"));
        var closeTime = ParseUnixMilliseconds(kline.GetProperty("T"));
        var isFinal = kline.TryGetProperty("x", out var finalProp) && finalProp.GetBoolean();
        var timestamp = isFinal ? closeTime : DateTime.UtcNow;

        await _eventBus.PublishAsync(new KlineEvent(timestamp, symbol, open, high, low, close, volume, startTime, closeTime, isFinal), ct).ConfigureAwait(false);
    }

    private static DateTime ParseUnixMilliseconds(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var value) => DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime,
            _ => DateTime.UtcNow
        };
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

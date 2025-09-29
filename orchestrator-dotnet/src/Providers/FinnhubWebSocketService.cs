using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

internal sealed class FinnhubWebSocketService : BackgroundService
{
    private static readonly TimeSpan MinReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromMinutes(2);

    private readonly ILogger<FinnhubWebSocketService> _logger;
    private readonly IEventBus _eventBus;
    private readonly IFreeModeConfigProvider _configProvider;

    public FinnhubWebSocketService(
        ILogger<FinnhubWebSocketService> logger,
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
                var config = _configProvider.Current;
                var url = ResolveWebSocketUrl(config);
                var symbols = ResolveSymbols(config);

                if (string.IsNullOrWhiteSpace(url) || symbols.Count == 0)
                {
                    _logger.LogWarning("Finnhub provider configuration incomplete; retrying in 10 minutes");
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await ConnectAndStreamAsync(url, symbols, stoppingToken).ConfigureAwait(false);
                delay = MinReconnectDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finnhub socket loop error; reconnecting in {Delay}s", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxReconnectDelay.TotalSeconds));
            }
        }
    }

    private static string ResolveWebSocketUrl(FreeModeConfig config)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(config.Stocks?.WsUrl))
        {
            urls.Add(ProviderUtils.ExpandPlaceholders(config.Stocks.WsUrl));
        }

        if (!string.IsNullOrWhiteSpace(config.Forex?.WsUrl))
        {
            urls.Add(ProviderUtils.ExpandPlaceholders(config.Forex.WsUrl));
        }

        return urls.FirstOrDefault() ?? string.Empty;
    }

    private static HashSet<string> ResolveSymbols(FreeModeConfig config)
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (config.Stocks?.Symbols is { Count: > 0 } stockSymbols)
        {
            foreach (var symbol in stockSymbols)
            {
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    symbols.Add(symbol.Trim().ToUpperInvariant());
                }
            }
        }

        if (config.Forex?.Pairs is { Count: > 0 } forexPairs)
        {
            foreach (var pair in forexPairs)
            {
                var normalized = NormalizeForexSymbol(pair);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    symbols.Add(normalized);
                }
            }
        }

        return symbols;
    }

    private static string NormalizeForexSymbol(string symbol)
    {
        var trimmed = symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (trimmed.Length == 6)
        {
            return $"OANDA:{trimmed[..3]}_{trimmed[3..]}";
        }

        return trimmed;
    }

    private async Task ConnectAndStreamAsync(string url, HashSet<string> symbols, CancellationToken cancellationToken)
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

        var uri = new Uri(url);
        _logger.LogInformation("Connecting to Finnhub WS {Uri}", uri);
        await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Connected to Finnhub WS {Uri}", uri);

        await SendSubscriptionsAsync(socket, symbols, cancellationToken).ConfigureAwait(false);

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
                    _logger.LogWarning("Finnhub WS requested close: {Status} - {Description}", result.CloseStatus, result.CloseStatusDescription);
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
                    await ProcessPayloadAsync(socket, payload, cancellationToken).ConfigureAwait(false);
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

    private static async Task SendSubscriptionsAsync(ClientWebSocket socket, IEnumerable<string> symbols, CancellationToken ct)
    {
        foreach (var symbol in symbols)
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["type"] = "subscribe",
                ["symbol"] = symbol
            });

            var bytes = Encoding.UTF8.GetBytes(payload);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessPayloadAsync(ClientWebSocket socket, string payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

            switch (type)
            {
                case "ping":
                    await SendPongAsync(socket, ct).ConfigureAwait(false);
                    break;
                case "trade":
                    await PublishTradesAsync(root, ct).ConfigureAwait(false);
                    break;
                case "error":
                    var msg = root.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "unknown";
                    _logger.LogWarning("Finnhub error: {Message}", msg);
                    break;
                default:
                    _logger.LogTrace("Unhandled Finnhub message type {Type}", type);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Finnhub payload: {Payload}", payload);
        }
    }

    private static async Task SendPongAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var payload = "{\"type\":\"pong\"}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private async ValueTask PublishTradesAsync(JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in dataArray.EnumerateArray())
        {
            var symbol = item.TryGetProperty("s", out var symbolProp) ? NormalizeInstrument(symbolProp.GetString()) : string.Empty;
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var price = ParseDecimal(item, "p");
            var quantity = ParseDecimal(item, "v");
            var timestamp = item.TryGetProperty("t", out var tsProp) && tsProp.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(tsProp.GetInt64()).UtcDateTime
                : DateTime.UtcNow;

            await _eventBus.PublishAsync(new TradeEvent(timestamp, symbol, price, quantity), ct).ConfigureAwait(false);
        }
    }

    private static string NormalizeInstrument(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (raw.StartsWith("OANDA:", StringComparison.OrdinalIgnoreCase))
        {
            return raw[6..].Replace('_', '/');
        }

        return raw.ToUpperInvariant();
    }

    private static decimal ParseDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
        {
            return 0m;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0m
        };
    }
}

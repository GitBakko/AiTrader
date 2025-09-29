using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orchestrator.Realtime;

public sealed class WebSocketConnectionManager
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _connections = new();
    private readonly ILogger<WebSocketConnectionManager> _logger;

    public WebSocketConnectionManager(ILogger<WebSocketConnectionManager> logger)
    {
        _logger = logger;
    }

    public Guid Register(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _connections[id] = socket;
        _logger.LogInformation("WebSocket connected {ConnectionId}", id);
        return id;
    }

    public async Task ListenAsync(Guid id, WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket receive failed for {ConnectionId}", id);
        }
        finally
        {
            await CloseAsync(id, socket).ConfigureAwait(false);
        }
    }

    public async Task BroadcastAsync(string payload, CancellationToken cancellationToken)
    {
        var buffer = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(buffer);

        foreach (var pair in _connections.ToArray())
        {
            var id = pair.Key;
            var socket = pair.Value;

            if (socket.State != WebSocketState.Open)
            {
                await CloseAsync(id, socket).ConfigureAwait(false);
                continue;
            }

            try
            {
                await socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast to {ConnectionId}", id);
                await CloseAsync(id, socket).ConfigureAwait(false);
            }
        }
    }

    private async Task CloseAsync(Guid id, WebSocket socket)
    {
        _connections.TryRemove(id, out _);

        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing websocket {ConnectionId}", id);
            }
        }

        socket.Dispose();
        _logger.LogInformation("WebSocket disconnected {ConnectionId}", id);
    }
}

using System.Collections.Concurrent;

namespace Orchestrator.Infra;

public interface IEvent
{
    DateTime TimestampUtc { get; }
}

public interface IEventBus
{
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler)
        where TEvent : class, IEvent;

    ValueTask PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;
}

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Func<IEvent, CancellationToken, ValueTask>>> _subscriptions = new();

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent);
        var subscriptionId = Guid.NewGuid();
        var map = _subscriptions.GetOrAdd(eventType, _ => new ConcurrentDictionary<Guid, Func<IEvent, CancellationToken, ValueTask>>());
        Func<IEvent, CancellationToken, ValueTask> wrapper = (evt, ct) => handler((TEvent)evt, ct);
        map[subscriptionId] = wrapper;
        return new Subscription(this, eventType, subscriptionId);
    }

    public async ValueTask PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(evt);

        var eventType = typeof(TEvent);
        if (!_subscriptions.TryGetValue(eventType, out var subscribers) || subscribers.Count == 0)
        {
            return;
        }

        foreach (var handler in subscribers.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler(evt, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Unsubscribe(Type eventType, Guid subscriptionId)
    {
        if (_subscriptions.TryGetValue(eventType, out var subscribers))
        {
            subscribers.TryRemove(subscriptionId, out _);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly InMemoryEventBus _bus;
        private readonly Type _eventType;
        private readonly Guid _subscriptionId;
        private bool _disposed;

        public Subscription(InMemoryEventBus bus, Type eventType, Guid subscriptionId)
        {
            _bus = bus;
            _eventType = eventType;
            _subscriptionId = subscriptionId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _bus.Unsubscribe(_eventType, _subscriptionId);
            _disposed = true;
        }
    }
}

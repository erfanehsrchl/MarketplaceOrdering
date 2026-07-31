using MarketplaceOrdering.Application.Common.Abstractions.Events;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Infrastructure.Events;

/// <summary>
/// A record of an Aggregate's committed Domain Events, in commit order.
/// </summary>
/// <param name="Sequence">
/// Monotonic append position. A real relay uses it as the cursor it advances
/// after a successful publish.
/// </param>
public sealed record DomainEventOutboxEntry(
    long Sequence,
    OrderId OrderId,
    long Version,
    IDomainEvent DomainEvent);

/// <summary>
/// In-memory stand-in for a transactional outbox table.
/// </summary>
/// <remarks>
/// It is written from inside the repository's critical section, so an event is
/// visible here exactly when the state that produced it is visible in the store.
/// Nothing drains it: the assignment does not require a broker, and inventing a
/// fake publisher would demonstrate less than showing the boundary an event
/// crosses. What it does prove is that no Domain Event is discarded.
/// </remarks>
public sealed class InMemoryDomainEventOutbox : IDomainEventOutbox
{
    private readonly object _syncRoot = new();
    private readonly List<DomainEventOutboxEntry> _entries = [];
    private long _sequence;

    public Result Append(
        OrderId orderId,
        long version,
        IReadOnlyCollection<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);
        lock (_syncRoot)
        {
            foreach (var domainEvent in domainEvents)
                _entries.Add(new DomainEventOutboxEntry(
                    ++_sequence, orderId, version, domainEvent));
            return Result.Success();
        }
    }

    public IReadOnlyList<DomainEventOutboxEntry> Read(OrderId? orderId = null)
    {
        lock (_syncRoot)
            return _entries
                .Where(entry => orderId is null || entry.OrderId == orderId)
                .ToArray();
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
            _sequence = 0;
        }
    }
}

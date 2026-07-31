using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Events;

/// <summary>
/// Where an Aggregate's pending Domain Events are durably recorded at the moment
/// its new state is persisted.
/// </summary>
/// <remarks>
/// <para>
/// Domain Events are facts about state that has just been committed. Publishing
/// them from the Handler after a successful save would leave a window where the
/// state exists and the event does not — a crash there loses the event, and
/// there is no way to tell afterwards. Writing them where the state is written,
/// under the same atomicity guarantee, removes that window: either both landed
/// or neither did.
/// </para>
/// <para>
/// Port contract: <see cref="Append"/> must participate in the same transaction
/// as the Aggregate write. In production that is an outbox table written by the
/// same database transaction, drained by a relay that publishes to the broker
/// with at-least-once delivery. Because delivery is at-least-once, consumers
/// must treat <c>EventId</c> as the deduplication key.
/// </para>
/// <para>
/// The method is synchronous precisely because it is not an independent
/// operation: it cannot be awaited separately from the write it belongs to
/// without reintroducing the window it exists to close.
/// </para>
/// </remarks>
public interface IDomainEventOutbox
{
    /// <param name="version">
    /// The Aggregate version the events were produced at. Gives consumers a
    /// total order per Order and lets a relay detect gaps.
    /// </param>
    Result Append(
        OrderId orderId,
        long version,
        IReadOnlyCollection<IDomainEvent> domainEvents);
}

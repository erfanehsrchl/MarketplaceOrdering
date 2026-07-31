namespace MarketplaceOrdering.Api.Contracts.Demo;

/// <summary>
/// One committed Domain Event as it would be handed to a message relay.
/// </summary>
/// <param name="EventType">
/// The Domain Event's type name — the routing key a broker would publish under.
/// </param>
/// <param name="EventId">
/// The deduplication key. Outbox delivery is at-least-once, so consumers are
/// expected to key on this rather than assume exactly-once.
/// </param>
public sealed record DomainEventOutboxEntryResponse(
    long Sequence,
    Guid OrderId,
    long OrderVersion,
    string EventType,
    Guid EventId,
    DateTimeOffset OccurredAt);

public sealed record DomainEventOutboxResponse(
    int Count,
    IReadOnlyList<DomainEventOutboxEntryResponse> Entries);

namespace MarketplaceOrdering.Domain.Shared;

public abstract record DomainEvent : IDomainEvent
{
    protected DomainEvent(DateTimeOffset occurredAt)
        : this(Guid.NewGuid(), occurredAt)
    {
    }

    protected DomainEvent(Guid eventId, DateTimeOffset occurredAt)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("A domain event identifier cannot be empty.", nameof(eventId));
        }

        EventId = eventId;
        OccurredAt = occurredAt;
    }

    public Guid EventId { get; }

    public DateTimeOffset OccurredAt { get; }
}

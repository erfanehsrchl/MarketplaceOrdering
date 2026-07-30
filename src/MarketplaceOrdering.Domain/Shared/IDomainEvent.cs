namespace MarketplaceOrdering.Domain.Shared;

public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

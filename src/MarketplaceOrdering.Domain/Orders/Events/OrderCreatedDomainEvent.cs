using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderCreatedDomainEvent : DomainEvent
{
    public OrderCreatedDomainEvent(
        OrderId orderId,
        CustomerId customerId,
        DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        OrderId = orderId;
        CustomerId = customerId;
    }

    public OrderId OrderId { get; }

    public CustomerId CustomerId { get; }
}

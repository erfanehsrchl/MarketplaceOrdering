using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderItemRemovedDomainEvent : DomainEvent
{
    public OrderItemRemovedDomainEvent(
        OrderId orderId,
        ProductId productId,
        Quantity removedQuantity,
        DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        OrderId = orderId;
        ProductId = productId;
        RemovedQuantity = removedQuantity;
    }

    public OrderId OrderId { get; }

    public ProductId ProductId { get; }

    public Quantity RemovedQuantity { get; }
}

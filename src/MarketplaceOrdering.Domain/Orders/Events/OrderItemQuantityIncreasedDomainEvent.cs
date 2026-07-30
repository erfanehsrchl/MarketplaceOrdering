using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderItemQuantityIncreasedDomainEvent : DomainEvent
{
    public OrderItemQuantityIncreasedDomainEvent(
        OrderId orderId,
        ProductId productId,
        Quantity previousQuantity,
        Quantity addedQuantity,
        Quantity resultingQuantity,
        DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        OrderId = orderId;
        ProductId = productId;
        PreviousQuantity = previousQuantity;
        AddedQuantity = addedQuantity;
        ResultingQuantity = resultingQuantity;
    }

    public OrderId OrderId { get; }

    public ProductId ProductId { get; }

    public Quantity PreviousQuantity { get; }

    public Quantity AddedQuantity { get; }

    public Quantity ResultingQuantity { get; }
}

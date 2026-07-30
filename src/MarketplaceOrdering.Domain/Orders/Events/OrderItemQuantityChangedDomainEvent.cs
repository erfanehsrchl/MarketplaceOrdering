using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderItemQuantityChangedDomainEvent : DomainEvent
{
    public OrderItemQuantityChangedDomainEvent(
        OrderId orderId,
        ProductId productId,
        Quantity previousQuantity,
        Quantity newQuantity,
        DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        OrderId = orderId;
        ProductId = productId;
        PreviousQuantity = previousQuantity;
        NewQuantity = newQuantity;
    }

    public OrderId OrderId { get; }

    public ProductId ProductId { get; }

    public Quantity PreviousQuantity { get; }

    public Quantity NewQuantity { get; }
}

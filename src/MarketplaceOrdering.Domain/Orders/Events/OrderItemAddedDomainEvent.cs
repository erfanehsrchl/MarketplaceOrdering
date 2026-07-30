using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderItemAddedDomainEvent : DomainEvent
{
    public OrderItemAddedDomainEvent(
        OrderId orderId,
        ProductId productId,
        ProductName productName,
        Quantity quantity,
        DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
    }

    public OrderId OrderId { get; }

    public ProductId ProductId { get; }

    public ProductName ProductName { get; }

    public Quantity Quantity { get; }
}

using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record DiscountCodeSelectedDomainEvent : DomainEvent
{
    public DiscountCodeSelectedDomainEvent(
        OrderId orderId,
        DiscountCode discountCode,
        DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        OrderId = orderId;
        DiscountCode = discountCode;
    }

    public OrderId OrderId { get; }

    public DiscountCode DiscountCode { get; }
}

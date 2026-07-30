using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record FulfillmentPlanCreatedDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    MoneyValue ProductsAmount,
    MoneyValue DiscountAmount,
    MoneyValue ShippingAmount,
    MoneyValue TotalPayable,
    int VendorCount,
    int MaximumDeliveryHours,
    DateTimeOffset At) : DomainEvent(At);

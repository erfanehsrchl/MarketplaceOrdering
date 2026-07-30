using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderExpiredDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    DateTimeOffset ExpiredAt,
    DateTimeOffset PaymentExpiresAt) : DomainEvent(ExpiredAt);

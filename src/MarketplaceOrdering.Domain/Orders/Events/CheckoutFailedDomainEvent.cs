using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record CheckoutFailedDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    string FailureCode,
    bool HasPendingCompensation,
    DateTimeOffset FailedAt) : DomainEvent(FailedAt);

using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderSubmittedForProcessingDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    DateTimeOffset At) : DomainEvent(At);

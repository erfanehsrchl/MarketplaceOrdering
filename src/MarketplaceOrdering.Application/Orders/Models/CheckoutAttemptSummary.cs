namespace MarketplaceOrdering.Application.Orders.Models;

public sealed record CheckoutAttemptSummary(
    Guid CheckoutAttemptId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? TotalPayable,
    DateTimeOffset? PaymentExpiresAt);

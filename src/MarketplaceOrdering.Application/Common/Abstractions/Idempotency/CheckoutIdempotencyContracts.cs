using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Application.Common.Abstractions.Idempotency;

public sealed record CheckoutOperationResult(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    OrderStatus Status,
    MoneyValue TotalPayable,
    DateTimeOffset PaymentExpiresAt,
    long Version);

public abstract record CheckoutIdempotencyClaim;

public sealed record CheckoutIdempotencyStarted : CheckoutIdempotencyClaim;
public sealed record CheckoutIdempotencyInProgress : CheckoutIdempotencyClaim;
public sealed record CheckoutIdempotencyConflict(
    OrderId ExistingOrderId) : CheckoutIdempotencyClaim;
public sealed record CheckoutIdempotencyCompleted(
    CheckoutOperationResult Result) : CheckoutIdempotencyClaim;
public sealed record CheckoutIdempotencyFailed(
    Error Error) : CheckoutIdempotencyClaim;

using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Idempotency;

public interface ICheckoutIdempotencyStore
{
    Task<Result<CheckoutIdempotencyClaim>> TryBeginAsync(
        IdempotencyKey idempotencyKey,
        OrderId orderId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<Result> CompleteAsync(
        IdempotencyKey idempotencyKey,
        CheckoutOperationResult result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<Result> FailAsync(
        IdempotencyKey idempotencyKey,
        Error error,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}

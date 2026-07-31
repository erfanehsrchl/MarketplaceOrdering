using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.Services;

/// <inheritdoc cref="ICheckoutIdempotencyGuard"/>
public sealed class CheckoutIdempotencyGuard : ICheckoutIdempotencyGuard
{
    private readonly ICheckoutIdempotencyStore _store;
    private readonly IClock _clock;

    public CheckoutIdempotencyGuard(
        ICheckoutIdempotencyStore store,
        IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public Task<Result<CheckoutIdempotencyClaim>> ClaimAsync(
        IdempotencyKey idempotencyKey,
        OrderId orderId,
        CheckoutAttemptId proposedCheckoutAttemptId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken) =>
        _store.TryBeginAsync(
            idempotencyKey,
            orderId,
            proposedCheckoutAttemptId,
            startedAt,
            cancellationToken);

    public async Task<Result<CheckoutOperationResult>> SucceedAsync(
        IdempotencyKey idempotencyKey,
        CheckoutOperationResult result,
        CancellationToken cancellationToken)
    {
        var completed = await _store.CompleteAsync(
            idempotencyKey, result, _clock.UtcNow, cancellationToken);
        return completed.IsSuccess
            ? Result<CheckoutOperationResult>.Success(result)
            : Result<CheckoutOperationResult>.Failure(
                CheckoutApplicationErrors.IdempotencyFinalizationFailed(
                    CheckoutMetadata.Of(
                        ("orderId", result.OrderId),
                        ("checkoutAttemptId", result.CheckoutAttemptId),
                        ("orderVersion", result.Version),
                        ("originalErrorCode", completed.Error.Code))));
    }

    public async Task<Result<CheckoutOperationResult>> FailAsync(
        IdempotencyKey idempotencyKey,
        Error originalError,
        CancellationToken cancellationToken)
    {
        var finalized = await _store.FailAsync(
            idempotencyKey, originalError, _clock.UtcNow, cancellationToken);
        return finalized.IsSuccess
            ? Result<CheckoutOperationResult>.Failure(originalError)
            : Result<CheckoutOperationResult>.Failure(
                CheckoutApplicationErrors.IdempotencyFinalizationFailed(
                    CheckoutMetadata.Of(
                        ("originalFailureCode", originalError.Code),
                        ("originalErrorCode", finalized.Error.Code))));
    }
}

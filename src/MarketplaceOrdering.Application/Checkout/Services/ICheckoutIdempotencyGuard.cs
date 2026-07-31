using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.Services;

/// <summary>
/// Owns the idempotency-key lifecycle of a Checkout: claim it, then close it as
/// succeeded or failed.
/// </summary>
/// <remarks>
/// <para>
/// Idempotency belongs to the use case, not to the transport. A retried HTTP
/// request is only one way the same Checkout can arrive twice; a queue redelivery
/// or an internal retry is another, and none of them go through a controller.
/// Keeping the guard here means every entry point inherits the protection.
/// </para>
/// <para>
/// Closing an entry can itself fail, and the failure is not cosmetic: the Order's
/// state is already committed, so the caller must learn that the recorded result
/// may not be replayable rather than see a generic error. That translation lives
/// here so the orchestrator does not repeat it at every exit.
/// </para>
/// </remarks>
public interface ICheckoutIdempotencyGuard
{
    /// <summary>
    /// Atomically claims the key. Whether this is a new Checkout, a replay, or a
    /// conflict is decided by the returned claim.
    /// </summary>
    Task<Result<CheckoutIdempotencyClaim>> ClaimAsync(
        IdempotencyKey idempotencyKey,
        OrderId orderId,
        CheckoutAttemptId proposedCheckoutAttemptId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the successful result so a replay returns it without reserving
    /// anything again.
    /// </summary>
    Task<Result<CheckoutOperationResult>> SucceedAsync(
        IdempotencyKey idempotencyKey,
        CheckoutOperationResult result,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a terminal failure and surfaces it. Every failing Checkout path
    /// ends here, so a replay repeats the original reason instead of restarting
    /// work whose side effects were already compensated.
    /// </summary>
    Task<Result<CheckoutOperationResult>> FailAsync(
        IdempotencyKey idempotencyKey,
        Error originalError,
        CancellationToken cancellationToken);
}

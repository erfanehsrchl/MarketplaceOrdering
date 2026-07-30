using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal sealed class FakeCheckoutIdempotencyStore
    : ICheckoutIdempotencyStore
{
    private readonly Dictionary<string, CheckoutIdempotencyClaim> _claims = [];
    internal CheckoutIdempotencyClaim? ClaimOverride { get; set; }
    internal Error? TryBeginFailure { get; set; }
    internal Error? CompleteFailure { get; set; }
    internal Error? FailFailure { get; set; }
    internal int TryBeginCalls { get; private set; }
    internal int CompleteCalls { get; private set; }
    internal int FailCalls { get; private set; }
    internal CheckoutAttemptId? CapturedProposedAttemptId { get; private set; }
    internal CheckoutOperationResult? CompletedResult { get; private set; }
    internal Error? StoredFailure { get; private set; }
    internal CancellationToken CapturedCancellationToken { get; private set; }
    internal IList<string>? Journal { get; set; }

    public Task<Result<CheckoutIdempotencyClaim>> TryBeginAsync(
        IdempotencyKey idempotencyKey,
        OrderId orderId,
        CheckoutAttemptId proposedCheckoutAttemptId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        TryBeginCalls++;
        Journal?.Add("Idempotency.TryBegin");
        CapturedProposedAttemptId = proposedCheckoutAttemptId;
        CapturedCancellationToken = cancellationToken;
        if (TryBeginFailure is not null)
            return Task.FromResult(
                Result<CheckoutIdempotencyClaim>.Failure(TryBeginFailure));
        if (ClaimOverride is not null)
            return Task.FromResult(
                Result<CheckoutIdempotencyClaim>.Success(ClaimOverride));
        if (_claims.TryGetValue(idempotencyKey.Value, out var stored))
        {
            var replay = stored is CheckoutIdempotencyStarted started
                ? new CheckoutIdempotencyInProgress(
                    started.OrderId, started.CheckoutAttemptId)
                : stored;
            return Task.FromResult(
                Result<CheckoutIdempotencyClaim>.Success(replay));
        }

        CheckoutIdempotencyClaim claim =
            new CheckoutIdempotencyStarted(
                orderId, proposedCheckoutAttemptId);
        _claims[idempotencyKey.Value] = claim;
        return Task.FromResult(
            Result<CheckoutIdempotencyClaim>.Success(claim));
    }

    public Task<Result> CompleteAsync(
        IdempotencyKey idempotencyKey,
        CheckoutOperationResult result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        CompleteCalls++;
        Journal?.Add("Idempotency.Complete");
        CompletedResult = result;
        CapturedCancellationToken = cancellationToken;
        if (CompleteFailure is not null)
            return Task.FromResult(Result.Failure(CompleteFailure));
        _claims[idempotencyKey.Value] =
            new CheckoutIdempotencyCompleted(result);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> FailAsync(
        IdempotencyKey idempotencyKey,
        Error error,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        FailCalls++;
        Journal?.Add("Idempotency.Fail");
        StoredFailure = error;
        CapturedCancellationToken = cancellationToken;
        if (FailFailure is not null)
            return Task.FromResult(Result.Failure(FailFailure));
        _claims[idempotencyKey.Value] =
            new CheckoutIdempotencyFailed(error);
        return Task.FromResult(Result.Success());
    }
}

using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Errors;

namespace MarketplaceOrdering.Infrastructure.Idempotency;

public sealed class InMemoryCheckoutIdempotencyStore
    : ICheckoutIdempotencyStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<IdempotencyKey, Entry> _entries = [];

    public void Reset()
    {
        lock (_syncRoot)
            _entries.Clear();
    }

    public Task<Result<CheckoutIdempotencyClaim>> TryBeginAsync(
        IdempotencyKey idempotencyKey,
        OrderId orderId,
        CheckoutAttemptId proposedCheckoutAttemptId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(idempotencyKey, out var entry))
            {
                _entries.Add(idempotencyKey, new Entry(
                    orderId, proposedCheckoutAttemptId,
                    IdempotencyStatus.InProgress, startedAt,
                    null, null, null));
                CheckoutIdempotencyClaim started =
                    new CheckoutIdempotencyStarted(
                        orderId, proposedCheckoutAttemptId);
                return Task.FromResult(
                    Result<CheckoutIdempotencyClaim>.Success(started));
            }

            CheckoutIdempotencyClaim claim =
                entry.OrderId != orderId
                    ? new CheckoutIdempotencyConflict(
                        entry.OrderId, entry.CheckoutAttemptId)
                    : entry.Status switch
                    {
                        IdempotencyStatus.InProgress =>
                            new CheckoutIdempotencyInProgress(
                                entry.OrderId, entry.CheckoutAttemptId),
                        IdempotencyStatus.Completed =>
                            new CheckoutIdempotencyCompleted(entry.Result!),
                        IdempotencyStatus.Failed =>
                            new CheckoutIdempotencyFailed(entry.Error!),
                        _ => throw new InvalidOperationException(
                            "Unknown idempotency status.")
                    };
            return Task.FromResult(
                Result<CheckoutIdempotencyClaim>.Success(claim));
        }
    }

    public Task<Result> CompleteAsync(
        IdempotencyKey idempotencyKey,
        CheckoutOperationResult result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        ArgumentNullException.ThrowIfNull(result);
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(idempotencyKey, out var entry))
                return Task.FromResult(Result.Failure(
                    InfrastructureErrors.IdempotencyEntryNotFound));
            if (entry.OrderId != result.OrderId
                || entry.CheckoutAttemptId != result.CheckoutAttemptId)
                return Task.FromResult(Result.Failure(
                    InfrastructureErrors.IdempotencyEntryConflict));
            if (entry.Status == IdempotencyStatus.Failed)
                return Task.FromResult(Result.Failure(
                    InfrastructureErrors.IdempotencyInvalidTransition));
            if (entry.Status == IdempotencyStatus.Completed)
                return Task.FromResult(entry.Result == result
                    ? Result.Success()
                    : Result.Failure(
                        InfrastructureErrors.IdempotencyEntryConflict));
            _entries[idempotencyKey] = entry with
            {
                Status = IdempotencyStatus.Completed,
                Result = result,
                TerminalAt = completedAt
            };
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result> FailAsync(
        IdempotencyKey idempotencyKey,
        Error error,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        ArgumentNullException.ThrowIfNull(error);
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(idempotencyKey, out var entry))
                return Task.FromResult(Result.Failure(
                    InfrastructureErrors.IdempotencyEntryNotFound));
            if (entry.Status == IdempotencyStatus.Completed)
                return Task.FromResult(Result.Failure(
                    InfrastructureErrors.IdempotencyInvalidTransition));
            if (entry.Status == IdempotencyStatus.Failed)
                return Task.FromResult(ErrorsEqual(entry.Error!, error)
                    ? Result.Success()
                    : Result.Failure(
                        InfrastructureErrors.IdempotencyEntryConflict));
            _entries[idempotencyKey] = entry with
            {
                Status = IdempotencyStatus.Failed,
                Error = error,
                TerminalAt = failedAt
            };
            return Task.FromResult(Result.Success());
        }
    }

    private static bool ErrorsEqual(Error left, Error right) =>
        left.Code == right.Code
        && left.Message == right.Message
        && left.Type == right.Type
        && left.Metadata.Count == right.Metadata.Count
        && left.Metadata.All(pair =>
            right.Metadata.TryGetValue(pair.Key, out var value)
            && value == pair.Value);

    private enum IdempotencyStatus
    {
        InProgress,
        Completed,
        Failed
    }

    private sealed record Entry(
        OrderId OrderId,
        CheckoutAttemptId CheckoutAttemptId,
        IdempotencyStatus Status,
        DateTimeOffset StartedAt,
        CheckoutOperationResult? Result,
        Error? Error,
        DateTimeOffset? TerminalAt);
}

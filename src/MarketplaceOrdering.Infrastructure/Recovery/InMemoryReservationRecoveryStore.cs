using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Errors;

namespace MarketplaceOrdering.Infrastructure.Recovery;

public sealed class InMemoryReservationRecoveryStore
    : IReservationRecoveryStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<ReservationOperationKey,
        ReservationRecoveryRecord> _pending = [];

    public Task<Result> UpsertAsync(
        ReservationRecoveryRecord record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);
        lock (_syncRoot)
        {
            if (_pending.TryGetValue(record.OperationKey, out var existing)
                && (existing.OrderId != record.OrderId
                    || existing.CheckoutAttemptId != record.CheckoutAttemptId
                    || existing.VendorId != record.VendorId
                    || existing.ReservationId != record.ReservationId))
                return Task.FromResult(Result.Failure(
                    InfrastructureErrors.RecoveryRecordConflict));
            _pending[record.OperationKey] = existing is null
                ? record with { }
                : record with { CreatedAt = existing.CreatedAt };
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result<IReadOnlyCollection<ReservationRecoveryRecord>>>
        GetPendingAsync(
            int maximumCount,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumCount <= 0)
            return Task.FromResult(
                Result<IReadOnlyCollection<ReservationRecoveryRecord>>.Failure(
                    InfrastructureErrors.RecoveryMaximumCountInvalid));
        lock (_syncRoot)
        {
            IReadOnlyCollection<ReservationRecoveryRecord> records = _pending
                .Values
                .OrderBy(record => record.CreatedAt)
                .ThenBy(record => record.OperationKey.Value,
                    StringComparer.Ordinal)
                .Take(maximumCount)
                .Select(record => record with { })
                .ToArray();
            return Task.FromResult(
                Result<IReadOnlyCollection<ReservationRecoveryRecord>>.Success(
                    records));
        }
    }

    public Task<Result> MarkResolvedAsync(
        ReservationOperationKey operationKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operationKey);
        lock (_syncRoot)
        {
            _pending.Remove(operationKey);
            return Task.FromResult(Result.Success());
        }
    }
}

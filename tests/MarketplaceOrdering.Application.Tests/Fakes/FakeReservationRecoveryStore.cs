using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal sealed class FakeReservationRecoveryStore
    : IReservationRecoveryStore
{
    private readonly Dictionary<string, ReservationRecoveryRecord> _records = [];
    internal Error? UpsertFailure { get; set; }
    internal int UpsertCalls { get; private set; }
    internal CancellationToken CapturedCancellationToken { get; private set; }
    internal IReadOnlyCollection<ReservationRecoveryRecord> Records =>
        _records.Values
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.OperationKey.Value, StringComparer.Ordinal)
            .ToArray();

    public Task<Result> UpsertAsync(
        ReservationRecoveryRecord record,
        CancellationToken cancellationToken)
    {
        UpsertCalls++;
        CapturedCancellationToken = cancellationToken;
        if (UpsertFailure is not null)
            return Task.FromResult(Result.Failure(UpsertFailure));
        _records[record.OperationKey.Value] = record;
        return Task.FromResult(Result.Success());
    }

    public Task<Result<IReadOnlyCollection<ReservationRecoveryRecord>>>
        GetPendingAsync(
            int maximumCount,
            CancellationToken cancellationToken)
    {
        CapturedCancellationToken = cancellationToken;
        IReadOnlyCollection<ReservationRecoveryRecord> records =
            Records.Take(maximumCount).ToArray();
        return Task.FromResult(
            Result<IReadOnlyCollection<ReservationRecoveryRecord>>
                .Success(records));
    }

    public Task<Result> MarkResolvedAsync(
        ReservationOperationKey operationKey,
        CancellationToken cancellationToken)
    {
        CapturedCancellationToken = cancellationToken;
        _records.Remove(operationKey.Value);
        return Task.FromResult(Result.Success());
    }
}

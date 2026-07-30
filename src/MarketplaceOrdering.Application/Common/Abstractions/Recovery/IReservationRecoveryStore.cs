using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Recovery;

public interface IReservationRecoveryStore
{
    Task<Result> UpsertAsync(
        ReservationRecoveryRecord record,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyCollection<ReservationRecoveryRecord>>> GetPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken);

    Task<Result> MarkResolvedAsync(
        ReservationOperationKey operationKey,
        CancellationToken cancellationToken);
}

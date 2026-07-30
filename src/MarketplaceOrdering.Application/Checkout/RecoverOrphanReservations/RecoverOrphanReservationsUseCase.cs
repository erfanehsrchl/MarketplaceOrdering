using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Checkout.RecoverOrphanReservations;

public sealed class RecoverOrphanReservationsUseCase
{
    private readonly IReservationRecoveryStore _recoveryStore;
    private readonly IInventoryReservationService _inventoryService;
    private readonly IClock _clock;

    public RecoverOrphanReservationsUseCase(
        IReservationRecoveryStore recoveryStore,
        IInventoryReservationService inventoryReservationService,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(recoveryStore);
        ArgumentNullException.ThrowIfNull(inventoryReservationService);
        ArgumentNullException.ThrowIfNull(clock);
        _recoveryStore = recoveryStore;
        _inventoryService = inventoryReservationService;
        _clock = clock;
    }

    public async Task<Result<RecoverOrphanReservationsResult>> ExecuteAsync(
        RecoverOrphanReservationsCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null || command.MaximumCount <= 0)
            return Result<RecoverOrphanReservationsResult>.Failure(
                ApplicationErrors.InvalidRequest);
        var pending = await _recoveryStore.GetPendingAsync(
            command.MaximumCount, cancellationToken);
        if (pending.IsFailure)
            return Result<RecoverOrphanReservationsResult>.Failure(
                pending.Error);
        var releasedCount = 0;
        var failedCount = 0;
        foreach (var record in pending.Value)
        {
            var released = await _inventoryService.ReleaseAsync(
                new InventoryReleaseRequest(
                    record.OrderId,
                    record.CheckoutAttemptId,
                    record.VendorId,
                    record.ReservationId),
                cancellationToken);
            if (released.IsSuccess
                && released.Value is InventoryReleaseSucceeded)
            {
                var resolved = await _recoveryStore.MarkResolvedAsync(
                    record.OperationKey, cancellationToken);
                if (resolved.IsFailure)
                    return Result<RecoverOrphanReservationsResult>.Failure(
                        resolved.Error);
                releasedCount++;
                continue;
            }

            var errorCode = released.IsFailure
                ? released.Error.Code
                : released.Value switch
                {
                    InventoryReleaseFailed failure => failure.ErrorCode,
                    InventoryReleaseIndeterminate indeterminate =>
                        indeterminate.ErrorCode,
                    _ => ApplicationErrors.DependencyOperationIndeterminate.Code
                };
            var updated = record with
            {
                LastErrorCode = errorCode,
                AttemptCount = record.AttemptCount + 1
            };
            _ = _clock.UtcNow;
            var stored = await _recoveryStore.UpsertAsync(
                updated, cancellationToken);
            if (stored.IsFailure)
                return Result<RecoverOrphanReservationsResult>.Failure(
                    stored.Error);
            failedCount++;
        }

        return Result<RecoverOrphanReservationsResult>.Success(
            new RecoverOrphanReservationsResult(
                pending.Value.Count,
                releasedCount,
                failedCount));
    }
}

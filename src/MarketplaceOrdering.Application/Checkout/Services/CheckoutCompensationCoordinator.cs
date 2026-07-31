using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.Services;

/// <inheritdoc cref="ICheckoutCompensationCoordinator"/>
public sealed class CheckoutCompensationCoordinator
    : ICheckoutCompensationCoordinator
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly IReservationRecoveryStore _reservationRecoveryStore;
    private readonly IReservationReleaseCoordinator _releaseCoordinator;
    private readonly IClock _clock;

    public CheckoutCompensationCoordinator(
        IOrderRepository orderRepository,
        IInventoryReservationService inventoryReservationService,
        IReservationRecoveryStore reservationRecoveryStore,
        IReservationReleaseCoordinator releaseCoordinator,
        IClock clock)
    {
        _orderRepository = orderRepository;
        _inventoryReservationService = inventoryReservationService;
        _reservationRecoveryStore = reservationRecoveryStore;
        _releaseCoordinator = releaseCoordinator;
        _clock = clock;
    }

    public async Task<Result> AbortBeforeReservationsAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        Error originalError,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        var failedAt = _clock.UtcNow;
        var failure = CheckoutFailure.Create(originalError.Code, failedAt);
        if (failure.IsFailure) return Result.Failure(failure.Error);
        var failed = order.FailCheckoutBeforeReservations(
            checkoutAttemptId, failure.Value, failedAt);
        if (failed.IsFailure) return failed;
        var saved = await _orderRepository.SaveAsync(order, cancellationToken);
        return saved.IsFailure ? Result.Failure(saved.Error) : Result.Success();
    }

    public async Task<Result> CompensateAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        Error originalError,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!HasReservationsToRelease(order))
            return await AbortBeforeReservationsAsync(
                order, checkoutAttemptId, originalError, cancellationToken);

        var failure = CheckoutFailure.Create(originalError.Code, _clock.UtcNow);
        if (failure.IsFailure) return Result.Failure(failure.Error);

        // The intent to compensate is persisted before any release runs, so a
        // crash mid-compensation is resumable instead of ambiguous.
        var begun = order.BeginCheckoutCompensation(
            checkoutAttemptId, failure.Value);
        if (begun.IsFailure) return begun;
        var compensatingSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (compensatingSave.IsFailure)
            return Result.Failure(compensatingSave.Error);

        return await ReleaseAndReturnToDraftAsync(
            order, checkoutAttemptId, cancellationToken);
    }

    public async Task ReconcilePersistedStateAsync(
        OrderId orderId,
        CheckoutAttemptId checkoutAttemptId,
        Error originalError,
        CancellationToken cancellationToken)
    {
        var reloaded = await _orderRepository.LoadAsync(
            orderId, cancellationToken);
        if (reloaded.IsFailure
            || reloaded.Value.CheckoutAttempt?.Id != checkoutAttemptId)
            return;

        var persistedOrder = reloaded.Value;
        if (!HasReservationsToRelease(persistedOrder))
        {
            await AbortBeforeReservationsAsync(
                persistedOrder, checkoutAttemptId,
                originalError, cancellationToken);
            return;
        }

        var failure = CheckoutFailure.Create(originalError.Code, _clock.UtcNow);
        if (failure.IsFailure
            || persistedOrder.BeginCheckoutCompensation(
                checkoutAttemptId, failure.Value).IsFailure)
            return;
        var saved = await _orderRepository.SaveAsync(
            persistedOrder, cancellationToken);
        if (saved.IsFailure) return;
        await ReleaseAndReturnToDraftAsync(
            persistedOrder, checkoutAttemptId, cancellationToken);
    }

    public async Task AbortPersistedStateAsync(
        OrderId orderId,
        CheckoutAttemptId checkoutAttemptId,
        Error originalError,
        CancellationToken cancellationToken)
    {
        var reloaded = await _orderRepository.LoadAsync(
            orderId, cancellationToken);
        if (reloaded.IsFailure
            || reloaded.Value.CheckoutAttempt?.Id != checkoutAttemptId
            || reloaded.Value.Status != OrderStatus.Processing)
            return;
        await AbortBeforeReservationsAsync(
            reloaded.Value, checkoutAttemptId, originalError, cancellationToken);
    }

    public async Task<Result> DiscardUnrecordedReservationAsync(
        OrderId orderId,
        CheckoutAttemptId checkoutAttemptId,
        VendorId vendorId,
        ReservationOperationKey operationKey,
        ReservationId reservationId,
        Error? persistenceError,
        CancellationToken cancellationToken)
    {
        var released = await _inventoryReservationService.ReleaseAsync(
            new InventoryReleaseRequest(
                orderId, checkoutAttemptId, vendorId, reservationId),
            cancellationToken);
        if (released.IsSuccess && released.Value is InventoryReleaseSucceeded)
            return Result.Success();

        var releaseErrorCode = ReleaseErrorCodeOf(released);
        var observedAt = _clock.UtcNow;
        var recorded = await _reservationRecoveryStore.UpsertAsync(
            new ReservationRecoveryRecord(
                orderId,
                checkoutAttemptId,
                vendorId,
                operationKey,
                reservationId,
                releaseErrorCode,
                observedAt,
                1,
                observedAt),
            cancellationToken);
        if (recorded.IsSuccess) return Result.Success();

        // Neither the Inventory service nor the recovery store accepted this
        // Reservation, so nothing in the system still refers to it. Surfacing
        // the identifiers is the only way it can be reconciled by hand.
        return Result.Failure(
            CheckoutApplicationErrors.RecoveryRecordFailed(
                CheckoutMetadata.Of(
                    ("orderId", orderId),
                    ("checkoutAttemptId", checkoutAttemptId),
                    ("vendorId", vendorId),
                    ("operationKey", operationKey.Value),
                    ("reservationId", reservationId),
                    ("persistenceErrorCode",
                        persistenceError?.Code ?? string.Empty),
                    ("releaseErrorCode", releaseErrorCode),
                    ("recoveryErrorCode", recorded.Error.Code))));
    }

    private async Task<Result> ReleaseAndReturnToDraftAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        CancellationToken cancellationToken)
    {
        var released = await _releaseCoordinator.ReleaseForFailedCheckoutAsync(
            order, checkoutAttemptId, cancellationToken);
        if (released.IsFailure) return Result.Failure(released.Error);
        var completed = order.CompleteCheckoutFailure(
            checkoutAttemptId, _clock.UtcNow);
        if (completed.IsFailure) return completed;
        var finalSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        return finalSave.IsFailure
            ? Result.Failure(finalSave.Error)
            : Result.Success();
    }

    private static bool HasReservationsToRelease(Order order) =>
        order.CheckoutAttempt?.Reservations.Any(reservation =>
            reservation.Status is InventoryReservationStatus.Active
                or InventoryReservationStatus.ReleasePending) == true;

    internal static string ReleaseErrorCodeOf(
        Result<InventoryReleaseOutcome> released) =>
        released.IsFailure
            ? released.Error.Code
            : released.Value switch
            {
                InventoryReleaseFailed failure => failure.ErrorCode,
                InventoryReleaseIndeterminate indeterminate =>
                    indeterminate.ErrorCode,
                _ => ApplicationErrors.DependencyOperationIndeterminate.Code
            };
}

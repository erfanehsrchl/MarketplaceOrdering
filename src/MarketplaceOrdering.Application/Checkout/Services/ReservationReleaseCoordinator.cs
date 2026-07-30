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

public sealed class ReservationReleaseCoordinator
{
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly IOrderRepository _orderRepository;
    private readonly IClock _clock;

    public ReservationReleaseCoordinator(
        IInventoryReservationService inventoryReservationService,
        IOrderRepository orderRepository,
        IReservationRecoveryStore reservationRecoveryStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(inventoryReservationService);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(reservationRecoveryStore);
        ArgumentNullException.ThrowIfNull(clock);
        _inventoryReservationService = inventoryReservationService;
        _orderRepository = orderRepository;
        _clock = clock;
    }

    public async Task<Result<long>> ReleaseForFailedCheckoutAsync(
        Order order,
        long currentVersion,
        CheckoutAttemptId checkoutAttemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        var attempt = order.CheckoutAttempt;
        if (attempt is null || attempt.Id != checkoutAttemptId)
            return Result<long>.Failure(CheckoutErrors.AttemptMismatch);

        var releasable = attempt.Reservations
            .Where(reservation =>
                reservation.ReservationId.HasValue
                && reservation.Status is InventoryReservationStatus.Active
                    or InventoryReservationStatus.ReleasePending)
            .OrderByDescending(reservation => reservation.ReservedAt)
            .ThenByDescending(reservation => reservation.VendorId.Value)
            .ToArray();

        var version = currentVersion;
        foreach (var reservation in releasable)
        {
            var released = await _inventoryReservationService.ReleaseAsync(
                new InventoryReleaseRequest(
                    order.Id,
                    checkoutAttemptId,
                    reservation.VendorId,
                    reservation.ReservationId!.Value),
                cancellationToken);
            var attemptedAt = _clock.UtcNow;
            Result recorded;
            if (released.IsSuccess
                && released.Value is InventoryReleaseSucceeded)
            {
                recorded = order.MarkInventoryReservationReleased(
                    checkoutAttemptId,
                    reservation.ReservationId.Value,
                    attemptedAt);
            }
            else
            {
                var errorCode = released.IsFailure
                    ? released.Error.Code
                    : released.Value switch
                    {
                        InventoryReleaseFailed failure => failure.ErrorCode,
                        InventoryReleaseIndeterminate indeterminate =>
                            indeterminate.ErrorCode,
                        _ => ApplicationErrors.DependencyOperationIndeterminate.Code
                    };
                recorded = order.MarkInventoryReservationReleasePending(
                    checkoutAttemptId,
                    reservation.ReservationId.Value,
                    errorCode,
                    attemptedAt);
            }

            if (recorded.IsFailure)
                return Result<long>.Failure(recorded.Error);
            var saved = await _orderRepository.SaveAsync(
                order, version, cancellationToken);
            if (saved.IsFailure)
                return Result<long>.Failure(saved.Error);
            version = saved.Value;
        }

        return Result<long>.Success(version);
    }
}

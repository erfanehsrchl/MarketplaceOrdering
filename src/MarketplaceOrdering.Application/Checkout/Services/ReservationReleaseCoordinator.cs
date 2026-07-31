using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.Services;

public sealed class ReservationReleaseCoordinator
    : IReservationReleaseCoordinator
{
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly IOrderRepository _orderRepository;
    private readonly IClock _clock;

    /// <remarks>
    /// This coordinator deliberately does not depend on
    /// <c>IReservationRecoveryStore</c>. Reservations it releases are already
    /// recorded on the Order, so a failed release becomes
    /// <c>ReleasePending</c> Aggregate state and is retried by
    /// <c>RetryPendingReservationReleases</c>. The recovery store exists for the
    /// opposite case only: a Reservation the external service confirmed that the
    /// Order never managed to persist, which no Aggregate can point at.
    /// </remarks>
    public ReservationReleaseCoordinator(
        IInventoryReservationService inventoryReservationService,
        IOrderRepository orderRepository,
        IClock clock)
    {
        _inventoryReservationService = inventoryReservationService;
        _orderRepository = orderRepository;
        _clock = clock;
    }

    public async Task<Result<long>> ReleaseForFailedCheckoutAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        var attempt = order.CheckoutAttempt;
        if (attempt is null || attempt.Id != checkoutAttemptId)
            return Result<long>.Failure(CheckoutErrors.AttemptMismatch);
        return await ReleaseAsync(
            order, checkoutAttemptId, cancellationToken);
    }

    public async Task<Result<long>> ReleaseForTerminalOrderAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Status is not (OrderStatus.Cancelled or OrderStatus.Expired))
            return Result<long>.Failure(CheckoutErrors.NotAllowed);
        if (order.CheckoutAttempt?.Id != checkoutAttemptId)
            return Result<long>.Failure(CheckoutErrors.AttemptMismatch);
        return await ReleaseAsync(
            order, checkoutAttemptId, cancellationToken);
    }

    private async Task<Result<long>> ReleaseAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        CancellationToken cancellationToken)
    {
        var attempt = order.CheckoutAttempt!;
        var releasable = attempt.Reservations
            .Where(reservation =>
                reservation.ReservationId.HasValue
                && reservation.Status is InventoryReservationStatus.Active
                    or InventoryReservationStatus.ReleasePending)
            .OrderByDescending(reservation => reservation.ReservedAt)
            .ThenByDescending(reservation => reservation.VendorId.Value)
            .ToArray();

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
                order, cancellationToken);
            if (saved.IsFailure)
                return Result<long>.Failure(saved.Error);
        }

        return Result<long>.Success(order.Version);
    }
}

using MediatR;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.RetryPendingReservationReleases;

public sealed class RetryPendingReservationReleasesCommandHandler
    : IRequestHandler<RetryPendingReservationReleasesCommand, Result<RetryPendingReservationReleasesResult>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IReservationReleaseCoordinator _releaseCoordinator;

    public RetryPendingReservationReleasesCommandHandler(
        IOrderRepository orderRepository,
        IReservationReleaseCoordinator releaseCoordinator)
    {
        _orderRepository = orderRepository;
        _releaseCoordinator = releaseCoordinator;
    }

    public async Task<Result<RetryPendingReservationReleasesResult>>
        Handle(
            RetryPendingReservationReleasesCommand command,
            CancellationToken cancellationToken)
    {
        if (command is null)
            return Result<RetryPendingReservationReleasesResult>.Failure(
                ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(command.OrderId);
        if (orderId.IsFailure)
            return Result<RetryPendingReservationReleasesResult>.Failure(
                orderId.Error);
        var loaded = await _orderRepository.LoadAsync(
            orderId.Value, cancellationToken);
        if (loaded.IsFailure)
            return Result<RetryPendingReservationReleasesResult>.Failure(
                loaded.Error);
        var order = loaded.Value;
        var attempt = order.CheckoutAttempt;
        if (attempt is null)
            return Result<RetryPendingReservationReleasesResult>.Failure(
                CheckoutErrors.AttemptNotFound);
        var pendingCount = attempt.Reservations.Count(reservation =>
            reservation.Status == InventoryReservationStatus.ReleasePending);
        if (pendingCount == 0)
            return Success(order, 0);

        Result<long> released;
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired)
        {
            released = await _releaseCoordinator
                .ReleaseForTerminalOrderAsync(
                    order,
                    attempt.Id,
                    cancellationToken);
        }
        else if (order.Status == OrderStatus.Draft
                 && attempt.Status ==
                 CheckoutAttemptStatus.CompensationPending)
        {
            released = await _releaseCoordinator
                .ReleaseForFailedCheckoutAsync(
                    order,
                    attempt.Id,
                    cancellationToken);
        }
        else
        {
            return Result<RetryPendingReservationReleasesResult>.Failure(
                CheckoutErrors.NotAllowed);
        }

        if (released.IsFailure)
            return Result<RetryPendingReservationReleasesResult>.Failure(
                released.Error);
        pendingCount = attempt.Reservations.Count(reservation =>
            reservation.Status == InventoryReservationStatus.ReleasePending);
        if (order.Status == OrderStatus.Draft
            && attempt.Status == CheckoutAttemptStatus.CompensationPending
            && pendingCount == 0)
        {
            var completed = order.CompletePendingCompensation(attempt.Id);
            if (completed.IsFailure)
                return Result<RetryPendingReservationReleasesResult>.Failure(
                    completed.Error);
            var saved = await _orderRepository.SaveAsync(
                order, cancellationToken);
            if (saved.IsFailure)
                return Result<RetryPendingReservationReleasesResult>.Failure(
                    saved.Error);
        }
        return Success(order, pendingCount);
    }

    private static Result<RetryPendingReservationReleasesResult> Success(
        Order order,
        int remaining) =>
        Result<RetryPendingReservationReleasesResult>.Success(
            new RetryPendingReservationReleasesResult(
                order.Id.Value,
                order.Status.ToString(),
                remaining,
                order.Version));
}

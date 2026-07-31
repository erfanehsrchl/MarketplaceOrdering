using MediatR;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Orders.CancelOrder;

public sealed class CancelOrderCommandHandler
    : IRequestHandler<CancelOrderCommand, Result<CancelOrderResult>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IClock _clock;
    private readonly ReservationReleaseCoordinator _releaseCoordinator;

    public CancelOrderCommandHandler(
        IOrderRepository orderRepository,
        IClock clock,
        ReservationReleaseCoordinator releaseCoordinator)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(releaseCoordinator);
        _orderRepository = orderRepository;
        _clock = clock;
        _releaseCoordinator = releaseCoordinator;
    }

    public async Task<Result<CancelOrderResult>> Handle(
        CancelOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            return Result<CancelOrderResult>.Failure(
                ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(command.OrderId);
        if (orderId.IsFailure)
            return Result<CancelOrderResult>.Failure(orderId.Error);
        var reason = CancellationReason.Create(command.Reason);
        if (reason.IsFailure)
            return Result<CancelOrderResult>.Failure(reason.Error);
        var loaded = await _orderRepository.LoadAsync(
            orderId.Value, cancellationToken);
        if (loaded.IsFailure)
            return Result<CancelOrderResult>.Failure(loaded.Error);
        var order = loaded.Value;
        var cancelled = order.Cancel(reason.Value, _clock.UtcNow);
        if (cancelled.IsFailure)
            return Result<CancelOrderResult>.Failure(cancelled.Error);
        var saved = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (saved.IsFailure)
            return Result<CancelOrderResult>.Failure(saved.Error);
        var attempt = order.CheckoutAttempt;
        if (attempt is not null && attempt.Reservations.Any(IsReleasable))
        {
            var released = await _releaseCoordinator
                .ReleaseForTerminalOrderAsync(
                    order, attempt.Id, cancellationToken);
            if (released.IsFailure)
                return Result<CancelOrderResult>.Failure(released.Error);
        }
        return Result<CancelOrderResult>.Success(
            new CancelOrderResult(
                order.Id.Value,
                order.Status.ToString(),
                order.Cancellation!.Reason.Value,
                order.Cancellation.CancelledAt,
                attempt?.Reservations.Any(reservation =>
                    reservation.Status ==
                        InventoryReservationStatus.ReleasePending) == true,
                order.Version));
    }

    private static bool IsReleasable(InventoryReservation reservation) =>
        reservation.Status is InventoryReservationStatus.Active
            or InventoryReservationStatus.ReleasePending;
}

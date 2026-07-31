using MediatR;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Orders.ExpireOrder;

public sealed class ExpireOrderCommandHandler
    : IRequestHandler<ExpireOrderCommand, Result<ExpireOrderResult>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IClock _clock;
    private readonly ReservationReleaseCoordinator _releaseCoordinator;

    public ExpireOrderCommandHandler(
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

    public async Task<Result<ExpireOrderResult>> Handle(
        ExpireOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            return Result<ExpireOrderResult>.Failure(
                ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(command.OrderId);
        if (orderId.IsFailure)
            return Result<ExpireOrderResult>.Failure(orderId.Error);
        var loaded = await _orderRepository.LoadAsync(
            orderId.Value, cancellationToken);
        if (loaded.IsFailure)
            return Result<ExpireOrderResult>.Failure(loaded.Error);
        var order = loaded.Value;
        var expired = order.Expire(_clock.UtcNow);
        if (expired.IsFailure)
            return Result<ExpireOrderResult>.Failure(expired.Error);
        var saved = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (saved.IsFailure)
            return Result<ExpireOrderResult>.Failure(saved.Error);
        var attempt = order.CheckoutAttempt!;
        if (attempt.Reservations.Any(reservation =>
                reservation.Status is InventoryReservationStatus.Active
                    or InventoryReservationStatus.ReleasePending))
        {
            var released = await _releaseCoordinator
                .ReleaseForTerminalOrderAsync(
                    order, attempt.Id, cancellationToken);
            if (released.IsFailure)
                return Result<ExpireOrderResult>.Failure(released.Error);
        }
        return Result<ExpireOrderResult>.Success(
            new ExpireOrderResult(
                order.Id.Value,
                order.Status.ToString(),
                order.ExpiredAt!.Value,
                attempt.Reservations.Any(reservation =>
                    reservation.Status ==
                        InventoryReservationStatus.ReleasePending),
                order.Version));
    }
}

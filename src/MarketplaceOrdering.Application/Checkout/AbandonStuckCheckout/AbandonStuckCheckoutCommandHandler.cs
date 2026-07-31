using MediatR;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.AbandonStuckCheckout;

/// <summary>
/// Recovers an Order stranded in <see cref="OrderStatus.Processing"/>.
/// </summary>
/// <remarks>
/// <para>
/// Checkout claims the Order before it calls anything external, so a crash — or
/// an Inventory reservation whose outcome never came back — leaves a claim that
/// nothing else can clear. This use case is the only writer allowed to break
/// such a claim, and only after <see cref="OrderPolicy.CheckoutAttemptTimeout"/>.
/// </para>
/// <para>
/// It never guesses. Every Reservation whose outcome is unknown is read back
/// from the Inventory service by its operation key first; only once every
/// outcome is known does it compensate and return the Order to Draft. If the
/// service still cannot answer, the Order stays claimed and the next run tries
/// again — a stuck Order is strictly better than silently leaked stock.
/// </para>
/// <para>
/// The idempotency entry needs no special handling: once the attempt is
/// <c>Failed</c>, a retry with the original key reconciles against the persisted
/// Order state and releases the key.
/// </para>
/// </remarks>
public sealed class AbandonStuckCheckoutCommandHandler
    : IRequestHandler<AbandonStuckCheckoutCommand,
        Result<AbandonStuckCheckoutResult>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly IReservationReleaseCoordinator _releaseCoordinator;
    private readonly IClock _clock;

    public AbandonStuckCheckoutCommandHandler(
        IOrderRepository orderRepository,
        IInventoryReservationService inventoryReservationService,
        IReservationReleaseCoordinator releaseCoordinator,
        IClock clock)
    {
        _orderRepository = orderRepository;
        _inventoryReservationService = inventoryReservationService;
        _releaseCoordinator = releaseCoordinator;
        _clock = clock;
    }

    public async Task<Result<AbandonStuckCheckoutResult>> Handle(
        AbandonStuckCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            return Result<AbandonStuckCheckoutResult>.Failure(
                ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(command.OrderId);
        if (orderId.IsFailure)
            return Result<AbandonStuckCheckoutResult>.Failure(orderId.Error);

        var loaded = await _orderRepository.LoadAsync(
            orderId.Value, cancellationToken);
        if (loaded.IsFailure)
            return Result<AbandonStuckCheckoutResult>.Failure(loaded.Error);

        var order = loaded.Value;
        var now = _clock.UtcNow;
        if (!order.IsCheckoutStuckAt(now))
            return Result<AbandonStuckCheckoutResult>.Failure(
                CheckoutErrors.NotStuck);

        var attempt = order.CheckoutAttempt!;
        var attemptId = attempt.Id;
        var resolved = await ResolveUnknownOutcomesAsync(
            order, attemptId, cancellationToken);
        if (resolved.IsFailure)
            return Result<AbandonStuckCheckoutResult>.Failure(resolved.Error);

        if (resolved.Value > 0)
        {
            var resolvedSave = await _orderRepository.SaveAsync(
                order, cancellationToken);
            if (resolvedSave.IsFailure)
                return Result<AbandonStuckCheckoutResult>.Failure(
                    resolvedSave.Error);
        }

        var compensated = await CompensateAsync(
            order, attemptId, cancellationToken);
        if (compensated.IsFailure)
            return Result<AbandonStuckCheckoutResult>.Failure(
                compensated.Error);

        return Result<AbandonStuckCheckoutResult>.Success(
            new AbandonStuckCheckoutResult(
                order.Id.Value,
                order.Status.ToString(),
                attemptId.Value,
                resolved.Value,
                attempt.Reservations.Count(reservation =>
                    reservation.Status ==
                        InventoryReservationStatus.ReleasePending),
                order.Version));
    }

    /// <summary>
    /// Turns every <c>Pending</c> Reservation into a known outcome by asking the
    /// Inventory service what its operation key produced.
    /// </summary>
    private async Task<Result<int>> ResolveUnknownOutcomesAsync(
        Order order,
        CheckoutAttemptId attemptId,
        CancellationToken cancellationToken)
    {
        var unknown = order.CheckoutAttempt!.Reservations
            .Where(reservation =>
                reservation.Status == InventoryReservationStatus.Pending)
            .OrderBy(reservation => reservation.VendorId.Value)
            .ToArray();

        var resolvedCount = 0;
        foreach (var reservation in unknown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await _inventoryReservationService.ResolveAsync(
                new InventoryReservationQuery(
                    order.Id,
                    attemptId,
                    reservation.VendorId,
                    reservation.OperationKey),
                cancellationToken);
            if (outcome.IsFailure)
                return Result<int>.Failure(outcome.Error);

            Result recorded;
            switch (outcome.Value)
            {
                case InventoryReservationSucceeded succeeded:
                    recorded = order.RecordInventoryReservationSucceeded(
                        attemptId,
                        reservation.OperationKey,
                        succeeded.ReservationId,
                        succeeded.ReservedAt);
                    break;
                case InventoryReservationRejected rejected:
                    recorded = order.RecordInventoryReservationRejected(
                        attemptId,
                        reservation.OperationKey,
                        rejected.FailureCode,
                        _clock.UtcNow);
                    break;
                default:
                    // The service cannot say whether stock was taken. Leaving
                    // the Order claimed is the only safe answer.
                    return Result<int>.Failure(
                        ApplicationErrors.DependencyOperationIndeterminate);
            }

            if (recorded.IsFailure)
                return Result<int>.Failure(recorded.Error);
            resolvedCount++;
        }

        return Result<int>.Success(resolvedCount);
    }

    private async Task<Result> CompensateAsync(
        Order order,
        CheckoutAttemptId attemptId,
        CancellationToken cancellationToken)
    {
        var attempt = order.CheckoutAttempt!;
        // A Reservation rejection encountered during resolution already recorded
        // the real cause; reuse it so the Order explains why Checkout ended.
        var failure = attempt.Failure ?? CheckoutFailure.Create(
            CheckoutErrors.AbandonedAfterTimeout.Code, _clock.UtcNow).Value;
        var begun = order.BeginCheckoutCompensation(attemptId, failure);
        if (begun.IsFailure) return begun;
        var compensatingSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (compensatingSave.IsFailure)
            return Result.Failure(compensatingSave.Error);

        var released = await _releaseCoordinator.ReleaseForFailedCheckoutAsync(
            order, attemptId, cancellationToken);
        if (released.IsFailure) return Result.Failure(released.Error);

        var completed = order.CompleteCheckoutFailure(attemptId, _clock.UtcNow);
        if (completed.IsFailure) return completed;
        var finalSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        return finalSave.IsFailure
            ? Result.Failure(finalSave.Error)
            : Result.Success();
    }
}

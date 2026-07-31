using MediatR;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Offers;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.CheckoutOrder;

/// <summary>
/// Turns a Draft Order into an Order awaiting payment.
/// </summary>
/// <remarks>
/// <para>
/// The orchestration order is the design, not an implementation detail:
/// </para>
/// <code>
/// claim the idempotency key
/// load the Order
/// claim the Order        (Draft -> Processing, saved immediately)
/// fetch Offers and the Discount policy
/// build and attach the Fulfillment Plan
/// reserve each Vendor    (intent persisted before the call, outcome after)
/// complete               (Processing -> AwaitingPayment)
/// close the idempotency key
/// </code>
/// <para>
/// Two rules make the sequence safe. The Order is claimed and saved <i>before</i>
/// any external call, so a second concurrent Checkout loses on the version check
/// rather than after it has already reserved stock. And the idempotency key is
/// closed <i>after</i> the final save, so a fast client retry can never land on
/// an Order still in <c>Processing</c>.
/// </para>
/// <para>
/// The handler itself only decides what happens next. Recording the idempotency
/// lifecycle belongs to <see cref="ICheckoutIdempotencyGuard"/>, undoing partial
/// work to <see cref="ICheckoutCompensationCoordinator"/>, and every business
/// rule to the Order Aggregate and the <see cref="FulfillmentPlanner"/>.
/// </para>
/// </remarks>
public sealed class CheckoutOrderCommandHandler
    : IRequestHandler<CheckoutOrderCommand, Result<CheckoutOperationResult>>
{
    /// <summary>
    /// Cleanup after the caller cancelled needs a token of its own; reusing the
    /// cancelled one would abort the release that stops stock from leaking.
    /// </summary>
    private static readonly TimeSpan PostReservationCleanupTimeout =
        TimeSpan.FromSeconds(5);

    private readonly IOrderRepository _orderRepository;
    private readonly IProductOfferProvider _productOfferProvider;
    private readonly IDiscountPolicyProvider _discountPolicyProvider;
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly ICheckoutIdempotencyGuard _idempotencyGuard;
    private readonly ICheckoutCompensationCoordinator _compensation;
    private readonly IClock _clock;
    private readonly FulfillmentPlanner _fulfillmentPlanner;

    public CheckoutOrderCommandHandler(
        IOrderRepository orderRepository,
        IProductOfferProvider productOfferProvider,
        IDiscountPolicyProvider discountPolicyProvider,
        IInventoryReservationService inventoryReservationService,
        ICheckoutIdempotencyGuard idempotencyGuard,
        ICheckoutCompensationCoordinator compensation,
        IClock clock,
        FulfillmentPlanner fulfillmentPlanner)
    {
        _orderRepository = orderRepository;
        _productOfferProvider = productOfferProvider;
        _discountPolicyProvider = discountPolicyProvider;
        _inventoryReservationService = inventoryReservationService;
        _idempotencyGuard = idempotencyGuard;
        _compensation = compensation;
        _clock = clock;
        _fulfillmentPlanner = fulfillmentPlanner;
    }

    public async Task<Result<CheckoutOperationResult>> Handle(
        CheckoutOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            return Result<CheckoutOperationResult>.Failure(
                ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(command.OrderId);
        if (orderId.IsFailure)
            return Result<CheckoutOperationResult>.Failure(orderId.Error);
        var idempotencyKey = IdempotencyKey.Create(command.IdempotencyKey);
        if (idempotencyKey.IsFailure)
            return Result<CheckoutOperationResult>.Failure(idempotencyKey.Error);

        var proposedAttemptId = CheckoutAttemptId.New();
        var startedAt = _clock.UtcNow;
        var claim = await _idempotencyGuard.ClaimAsync(
            idempotencyKey.Value,
            orderId.Value,
            proposedAttemptId,
            startedAt,
            cancellationToken);
        if (claim.IsFailure)
            return Result<CheckoutOperationResult>.Failure(claim.Error);

        return claim.Value switch
        {
            CheckoutIdempotencyCompleted completed =>
                Result<CheckoutOperationResult>.Success(completed.Result),
            CheckoutIdempotencyFailed failed =>
                Result<CheckoutOperationResult>.Failure(failed.Error),
            CheckoutIdempotencyConflict conflict =>
                Result<CheckoutOperationResult>.Failure(
                    CheckoutApplicationErrors.IdempotencyConflict(
                        CheckoutMetadata.Of(
                            ("requestedOrderId", orderId.Value),
                            ("existingOrderId", conflict.ExistingOrderId),
                            ("existingCheckoutAttemptId",
                                conflict.ExistingCheckoutAttemptId)))),
            CheckoutIdempotencyInProgress inProgress =>
                await ReconcileInProgressAsync(
                    idempotencyKey.Value, inProgress, cancellationToken),
            CheckoutIdempotencyStarted started =>
                await ExecuteAsync(
                    idempotencyKey.Value,
                    started.OrderId,
                    started.CheckoutAttemptId,
                    startedAt,
                    cancellationToken),
            _ => Result<CheckoutOperationResult>.Failure(
                ApplicationErrors.DependencyOperationIndeterminate)
        };
    }

    private async Task<Result<CheckoutOperationResult>> ExecuteAsync(
        IdempotencyKey idempotencyKey,
        OrderId orderId,
        CheckoutAttemptId attemptId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await _orderRepository.LoadAsync(orderId, cancellationToken);
        if (loaded.IsFailure)
            return await _idempotencyGuard.FailAsync(
                idempotencyKey, loaded.Error, cancellationToken);

        var order = loaded.Value;

        // Claim the Order before touching anything external. A losing concurrent
        // Checkout fails here, having reserved nothing.
        var started = order.StartCheckout(attemptId, startedAt);
        if (started.IsFailure)
            return await _idempotencyGuard.FailAsync(
                idempotencyKey, started.Error, cancellationToken);
        var processingSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (processingSave.IsFailure)
            return await _idempotencyGuard.FailAsync(
                idempotencyKey, processingSave.Error, cancellationToken);

        var planned = await AttachPlanAsync(
            order, attemptId, cancellationToken);
        if (planned.Error is { } planningError)
            return await AbortAsync(
                idempotencyKey, order, orderId, attemptId,
                planningError, planned.PersistenceFailed, cancellationToken);

        var reserved = await ReserveEveryVendorAsync(
            order, attemptId, cancellationToken);
        if (reserved is { } reservationFailure)
            return await FinishFailedReservationAsync(
                idempotencyKey, order, orderId, attemptId,
                reservationFailure, cancellationToken);

        return await CompleteAsync(
            idempotencyKey, order, orderId, attemptId, cancellationToken);
    }

    /// <summary>
    /// Resolves prices, computes the best Fulfillment Plan, and attaches it.
    /// </summary>
    private async Task<PlanningOutcome> AttachPlanAsync(
        Order order,
        CheckoutAttemptId attemptId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var demands = order.GetDemandSnapshot();
        var offers = await _productOfferProvider.GetOffersAsync(
            demands, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (offers.IsFailure) return PlanningOutcome.Rejected(offers.Error);

        DiscountPolicy? discountPolicy = null;
        if (order.SelectedDiscount is { } selectedDiscount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Re-fetched rather than snapshotted at apply time: a policy that has
            // since been withdrawn must not still be honoured.
            var policy = await _discountPolicyProvider.GetByCodeAsync(
                selectedDiscount.Code, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (policy.IsFailure) return PlanningOutcome.Rejected(policy.Error);
            discountPolicy = policy.Value;
        }

        var plan = _fulfillmentPlanner.CreateBestPlan(
            demands, offers.Value, discountPolicy, _clock.UtcNow);
        if (plan.IsFailure) return PlanningOutcome.Rejected(plan.Error);

        var attached = order.AttachFulfillmentPlan(
            attemptId, plan.Value, _clock.UtcNow);
        if (attached.IsFailure)
            return PlanningOutcome.Rejected(attached.Error);

        var planSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        return planSave.IsFailure
            ? PlanningOutcome.NotPersisted(planSave.Error)
            : PlanningOutcome.Succeeded;
    }

    /// <summary>
    /// Reserves each Vendor in the Plan, in Vendor order.
    /// </summary>
    /// <returns>
    /// <c>null</c> when every Vendor is reserved, otherwise how it failed.
    /// </returns>
    /// <remarks>
    /// Intent is persisted before each call and the outcome immediately after,
    /// so no window exists in which the Inventory service holds stock the Order
    /// has no record of. The one case that cannot be closed this way — the call
    /// succeeded but its result could not be saved — is handed to the
    /// compensation coordinator, which releases it or records it as an orphan.
    /// </remarks>
    private async Task<ReservationFailure?> ReserveEveryVendorAsync(
        Order order,
        CheckoutAttemptId attemptId,
        CancellationToken cancellationToken)
    {
        var plan = order.CheckoutAttempt!.FulfillmentPlan!;
        foreach (var vendor in plan.Vendors
                     .OrderBy(vendor => vendor.VendorId.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationKey = ReservationOperationKey.For(
                order.Id, attemptId, vendor.VendorId);

            var intent = order.BeginInventoryReservation(
                attemptId, vendor.VendorId, operationKey, _clock.UtcNow);
            if (intent.IsFailure)
                return ReservationFailure.NeedsCompensation(intent.Error);
            var intentSave = await _orderRepository.SaveAsync(
                order, cancellationToken);
            if (intentSave.IsFailure)
                return ReservationFailure.PersistedStateUnknown(
                    intentSave.Error);

            cancellationToken.ThrowIfCancellationRequested();
            var reserved = await _inventoryReservationService.ReserveAsync(
                BuildReservationRequest(
                    order.Id, attemptId, vendor, operationKey),
                cancellationToken);

            // A cancelled caller does not undo a Reservation that already
            // happened; it still has to be given back.
            if (cancellationToken.IsCancellationRequested
                && reserved.IsSuccess
                && reserved.Value is InventoryReservationSucceeded cancelled)
            {
                using var cleanupCancellation = new CancellationTokenSource(
                    PostReservationCleanupTimeout);
                await _compensation.DiscardUnrecordedReservationAsync(
                    order.Id, attemptId, vendor.VendorId, operationKey,
                    cancelled.ReservationId, null, cleanupCancellation.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (reserved.IsFailure)
                return ReservationFailure.Indeterminate(
                    IndeterminateReservationError(
                        order.Id, attemptId, vendor.VendorId,
                        operationKey, reserved.Error.Code));

            switch (reserved.Value)
            {
                case InventoryReservationIndeterminate indeterminate:
                    return ReservationFailure.Indeterminate(
                        IndeterminateReservationError(
                            order.Id, attemptId, vendor.VendorId,
                            operationKey, indeterminate.FailureCode));

                case InventoryReservationRejected rejected:
                {
                    var recorded = order.RecordInventoryReservationRejected(
                        attemptId, operationKey,
                        rejected.FailureCode, _clock.UtcNow);
                    if (recorded.IsFailure)
                        return ReservationFailure.NeedsCompensation(
                            recorded.Error);
                    var rejectionSave = await _orderRepository.SaveAsync(
                        order, cancellationToken);
                    if (rejectionSave.IsFailure)
                        return ReservationFailure.Reported(rejectionSave.Error);
                    return ReservationFailure.NeedsCompensation(
                        Error.DependencyFailure(
                            order.CheckoutAttempt!.Failure!.Code,
                            "Inventory rejected the Reservation."));
                }

                case InventoryReservationSucceeded succeeded:
                {
                    var recorded = order.RecordInventoryReservationSucceeded(
                        attemptId, operationKey,
                        succeeded.ReservationId, succeeded.ReservedAt);
                    var successSave = recorded.IsSuccess
                        ? await _orderRepository.SaveAsync(
                            order, cancellationToken)
                        : Result<long>.Failure(recorded.Error);
                    if (successSave.IsFailure)
                        return await DiscardAndReportAsync(
                            order.Id, attemptId, vendor.VendorId, operationKey,
                            succeeded.ReservationId, successSave.Error,
                            cancellationToken);
                    break;
                }

                default:
                    return ReservationFailure.Indeterminate(
                        IndeterminateReservationError(
                            order.Id, attemptId, vendor.VendorId, operationKey,
                            ApplicationErrors
                                .DependencyOperationIndeterminate.Code));
            }
        }

        return null;
    }

    private async Task<ReservationFailure> DiscardAndReportAsync(
        OrderId orderId,
        CheckoutAttemptId attemptId,
        VendorId vendorId,
        ReservationOperationKey operationKey,
        ReservationId reservationId,
        Error persistenceError,
        CancellationToken cancellationToken)
    {
        var discarded = await _compensation.DiscardUnrecordedReservationAsync(
            orderId, attemptId, vendorId, operationKey,
            reservationId, persistenceError, cancellationToken);
        return discarded.IsFailure
            ? ReservationFailure.Reported(discarded.Error)
            : ReservationFailure.PersistedStateAborted(persistenceError);
    }

    private async Task<Result<CheckoutOperationResult>> CompleteAsync(
        IdempotencyKey idempotencyKey,
        Order order,
        OrderId orderId,
        CheckoutAttemptId attemptId,
        CancellationToken cancellationToken)
    {
        var completed = order.CompleteCheckout(attemptId, _clock.UtcNow);
        if (completed.IsFailure)
            return await FinishFailedReservationAsync(
                idempotencyKey, order, orderId, attemptId,
                ReservationFailure.NeedsCompensation(completed.Error),
                cancellationToken);

        var completedSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (completedSave.IsFailure)
        {
            await _compensation.ReconcilePersistedStateAsync(
                orderId, attemptId, completedSave.Error, cancellationToken);
            return await _idempotencyGuard.FailAsync(
                idempotencyKey, completedSave.Error, cancellationToken);
        }

        var attempt = order.CheckoutAttempt!;
        var result = new CheckoutOperationResult(
            order.Id,
            attemptId,
            order.Status,
            attempt.FulfillmentPlan!.TotalPayable,
            attempt.PaymentExpiresAt!.Value,
            order.Version);
        cancellationToken.ThrowIfCancellationRequested();
        return await _idempotencyGuard.SucceedAsync(
            idempotencyKey, result, cancellationToken);
    }

    private async Task<Result<CheckoutOperationResult>> AbortAsync(
        IdempotencyKey idempotencyKey,
        Order order,
        OrderId orderId,
        CheckoutAttemptId attemptId,
        Error originalError,
        bool persistedStateUnknown,
        CancellationToken cancellationToken)
    {
        if (persistedStateUnknown)
        {
            await _compensation.AbortPersistedStateAsync(
                orderId, attemptId, originalError, cancellationToken);
            return await _idempotencyGuard.FailAsync(
                idempotencyKey, originalError, cancellationToken);
        }

        var aborted = await _compensation.AbortBeforeReservationsAsync(
            order, attemptId, originalError, cancellationToken);
        return await _idempotencyGuard.FailAsync(
            idempotencyKey,
            aborted.IsFailure ? aborted.Error : originalError,
            cancellationToken);
    }

    private async Task<Result<CheckoutOperationResult>>
        FinishFailedReservationAsync(
            IdempotencyKey idempotencyKey,
            Order order,
            OrderId orderId,
            CheckoutAttemptId attemptId,
            ReservationFailure failure,
            CancellationToken cancellationToken)
    {
        switch (failure.Disposition)
        {
            // The Order is still claimed and the Inventory outcome is unknown.
            // Nothing is compensated and the key stays open on purpose: only
            // stuck-Checkout recovery, which can read the outcome back, is
            // allowed to resolve this.
            case FailureDisposition.LeaveClaimed:
                return Result<CheckoutOperationResult>.Failure(failure.Error);

            case FailureDisposition.AlreadyHandled:
                return await _idempotencyGuard.FailAsync(
                    idempotencyKey, failure.Error, cancellationToken);

            case FailureDisposition.ReconcilePersisted:
                await _compensation.ReconcilePersistedStateAsync(
                    orderId, attemptId, failure.Error, cancellationToken);
                return await _idempotencyGuard.FailAsync(
                    idempotencyKey, failure.Error, cancellationToken);

            // The Reservation was already released or handed to recovery, so
            // the persisted Order only needs returning to Draft. Compensating
            // again would release the same Reservation twice.
            case FailureDisposition.AbortPersisted:
                await _compensation.AbortPersistedStateAsync(
                    orderId, attemptId, failure.Error, cancellationToken);
                return await _idempotencyGuard.FailAsync(
                    idempotencyKey, failure.Error, cancellationToken);

            default:
                var compensated = await _compensation.CompensateAsync(
                    order, attemptId, failure.Error, cancellationToken);
                return await _idempotencyGuard.FailAsync(
                    idempotencyKey,
                    compensated.IsFailure ? compensated.Error : failure.Error,
                    cancellationToken);
        }
    }

    /// <summary>
    /// A replay arriving while an entry is still open. The persisted Order is
    /// the source of truth: if the original run actually finished, its result is
    /// reconstructed and the entry repaired rather than left open forever.
    /// </summary>
    private async Task<Result<CheckoutOperationResult>> ReconcileInProgressAsync(
        IdempotencyKey idempotencyKey,
        CheckoutIdempotencyInProgress claim,
        CancellationToken cancellationToken)
    {
        var loaded = await _orderRepository.LoadAsync(
            claim.OrderId, cancellationToken);
        if (loaded.IsFailure)
            return Result<CheckoutOperationResult>.Failure(
                CheckoutApplicationErrors.IdempotencyInProgress(
                    CheckoutMetadata.Of(
                        ("orderId", claim.OrderId),
                        ("checkoutAttemptId", claim.CheckoutAttemptId),
                        ("loadErrorCode", loaded.Error.Code))));

        var order = loaded.Value;
        var attempt = order.CheckoutAttempt;
        if (order.Status == OrderStatus.AwaitingPayment
            && attempt?.Id == claim.CheckoutAttemptId
            && attempt.Status == CheckoutAttemptStatus.Completed
            && attempt.FulfillmentPlan is not null
            && attempt.PaymentExpiresAt.HasValue)
            return await _idempotencyGuard.SucceedAsync(
                idempotencyKey,
                new CheckoutOperationResult(
                    order.Id,
                    claim.CheckoutAttemptId,
                    order.Status,
                    attempt.FulfillmentPlan.TotalPayable,
                    attempt.PaymentExpiresAt.Value,
                    order.Version),
                cancellationToken);

        if (attempt?.Id == claim.CheckoutAttemptId
            && attempt.Status is CheckoutAttemptStatus.Failed
                or CheckoutAttemptStatus.CompensationPending
            && attempt.Failure is not null)
            return await _idempotencyGuard.FailAsync(
                idempotencyKey,
                CheckoutFailureRehydrator.Rehydrate(attempt.Failure.Code),
                cancellationToken);

        return Result<CheckoutOperationResult>.Failure(
            CheckoutApplicationErrors.IdempotencyInProgress(
                CheckoutMetadata.Of(
                    ("orderId", claim.OrderId),
                    ("checkoutAttemptId", claim.CheckoutAttemptId),
                    ("attemptStatus", attempt?.Status.ToString() ?? "missing"))));
    }

    private static InventoryReservationRequest BuildReservationRequest(
        OrderId orderId,
        CheckoutAttemptId attemptId,
        VendorFulfillment vendor,
        ReservationOperationKey operationKey)
    {
        var items = vendor.ProductAllocations
            .GroupBy(allocation => allocation.ProductId)
            .OrderBy(group => group.Key.Value)
            .Select(group => new InventoryReservationItem(
                group.Key,
                Quantity.Create(group.Sum(
                    allocation => allocation.Quantity.Value)).Value))
            .ToArray();
        return new InventoryReservationRequest(
            orderId, attemptId, vendor.VendorId, operationKey, items);
    }

    private static Error IndeterminateReservationError(
        OrderId orderId,
        CheckoutAttemptId attemptId,
        VendorId vendorId,
        ReservationOperationKey operationKey,
        string dependencyErrorCode) =>
        CheckoutApplicationErrors.ReservationOutcomeIndeterminate(
            CheckoutMetadata.Of(
                ("orderId", orderId),
                ("checkoutAttemptId", attemptId),
                ("vendorId", vendorId),
                ("operationKey", operationKey.Value),
                ("dependencyErrorCode", dependencyErrorCode)));

    /// <summary>What planning produced, and whether the store still agrees.</summary>
    private readonly record struct PlanningOutcome(
        Error? Error,
        bool PersistenceFailed)
    {
        internal static PlanningOutcome Succeeded => new(null, false);

        internal static PlanningOutcome Rejected(Error error) =>
            new(error, false);

        internal static PlanningOutcome NotPersisted(Error error) =>
            new(error, true);
    }

    private enum FailureDisposition
    {
        /// <summary>Confirmed Reservations exist and must be released.</summary>
        Compensate,

        /// <summary>
        /// The Inventory outcome is unknown, so the claim is deliberately kept.
        /// </summary>
        LeaveClaimed,

        /// <summary>
        /// The persisted Order may differ from the one in memory; re-read it
        /// before compensating.
        /// </summary>
        ReconcilePersisted,

        /// <summary>
        /// The Reservation has already been disposed of; the persisted Order
        /// only needs returning to Draft.
        /// </summary>
        AbortPersisted,

        /// <summary>Cleanup already ran; only the key still needs closing.</summary>
        AlreadyHandled
    }

    private readonly record struct ReservationFailure(
        Error Error,
        FailureDisposition Disposition)
    {
        internal static ReservationFailure NeedsCompensation(Error error) =>
            new(error, FailureDisposition.Compensate);

        internal static ReservationFailure Indeterminate(Error error) =>
            new(error, FailureDisposition.LeaveClaimed);

        internal static ReservationFailure PersistedStateUnknown(Error error) =>
            new(error, FailureDisposition.ReconcilePersisted);

        internal static ReservationFailure PersistedStateAborted(Error error) =>
            new(error, FailureDisposition.AbortPersisted);

        internal static ReservationFailure Reported(Error error) =>
            new(error, FailureDisposition.AlreadyHandled);
    }
}

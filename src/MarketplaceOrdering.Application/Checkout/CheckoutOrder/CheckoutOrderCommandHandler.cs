using MediatR;
using System.Globalization;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Offers;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.CheckoutOrder;

public sealed class CheckoutOrderCommandHandler
    : IRequestHandler<CheckoutOrderCommand, Result<CheckoutOperationResult>>
{
    private static readonly TimeSpan PostReservationCleanupTimeout =
        TimeSpan.FromSeconds(5);
    private readonly IOrderRepository _orderRepository;
    private readonly IProductOfferProvider _productOfferProvider;
    private readonly IDiscountPolicyProvider _discountPolicyProvider;
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly ICheckoutIdempotencyStore _idempotencyStore;
    private readonly IReservationRecoveryStore _reservationRecoveryStore;
    private readonly IClock _clock;
    private readonly FulfillmentPlanner _fulfillmentPlanner;
    private readonly IReservationReleaseCoordinator _releaseCoordinator;

    public CheckoutOrderCommandHandler(
        IOrderRepository orderRepository,
        IProductOfferProvider productOfferProvider,
        IDiscountPolicyProvider discountPolicyProvider,
        IInventoryReservationService inventoryReservationService,
        ICheckoutIdempotencyStore idempotencyStore,
        IReservationRecoveryStore reservationRecoveryStore,
        IClock clock,
        FulfillmentPlanner fulfillmentPlanner,
        IReservationReleaseCoordinator releaseCoordinator)
    {
        _orderRepository = orderRepository;
        _productOfferProvider = productOfferProvider;
        _discountPolicyProvider = discountPolicyProvider;
        _inventoryReservationService = inventoryReservationService;
        _idempotencyStore = idempotencyStore;
        _reservationRecoveryStore = reservationRecoveryStore;
        _clock = clock;
        _fulfillmentPlanner = fulfillmentPlanner;
        _releaseCoordinator = releaseCoordinator;
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
        var claim = await _idempotencyStore.TryBeginAsync(
            idempotencyKey.Value,
            orderId.Value,
            proposedAttemptId,
            startedAt,
            cancellationToken);
        if (claim.IsFailure)
            return Result<CheckoutOperationResult>.Failure(claim.Error);

        switch (claim.Value)
        {
            case CheckoutIdempotencyCompleted completed:
                return Result<CheckoutOperationResult>.Success(completed.Result);
            case CheckoutIdempotencyFailed failed:
                return Result<CheckoutOperationResult>.Failure(failed.Error);
            case CheckoutIdempotencyConflict conflict:
                return Result<CheckoutOperationResult>.Failure(
                    CheckoutApplicationErrors.IdempotencyConflict(
                        Metadata(
                            ("requestedOrderId", orderId.Value),
                            ("existingOrderId", conflict.ExistingOrderId),
                            ("existingCheckoutAttemptId",
                                conflict.ExistingCheckoutAttemptId))));
            case CheckoutIdempotencyInProgress inProgress:
                return await ReconcileInProgressAsync(
                    idempotencyKey.Value,
                    inProgress,
                    cancellationToken);
            case CheckoutIdempotencyStarted started:
                return await ExecuteNewCheckoutAsync(
                    idempotencyKey.Value,
                    started.OrderId,
                    started.CheckoutAttemptId,
                    startedAt,
                    cancellationToken);
            default:
                return Result<CheckoutOperationResult>.Failure(
                    ApplicationErrors.DependencyOperationIndeterminate);
        }
    }

    private async Task<Result<CheckoutOperationResult>> ExecuteNewCheckoutAsync(
        IdempotencyKey idempotencyKey,
        OrderId orderId,
        CheckoutAttemptId attemptId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await _orderRepository.LoadAsync(
            orderId, cancellationToken);
        if (loaded.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, loaded.Error, cancellationToken);

        var order = loaded.Value;
        var started = order.StartCheckout(attemptId, startedAt);
        if (started.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, started.Error, cancellationToken);
        var processingSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (processingSave.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, processingSave.Error, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var demands = order.GetDemandSnapshot();
        var offers = await _productOfferProvider.GetOffersAsync(
            demands, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (offers.IsFailure)
            return await FailBeforeReservationsAndFinalizeAsync(
                idempotencyKey, order, attemptId,
                offers.Error, cancellationToken);

        DiscountPolicy? discountPolicy = null;
        if (order.SelectedDiscount is { } selectedDiscount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var policy = await _discountPolicyProvider.GetByCodeAsync(
                selectedDiscount.Code, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (policy.IsFailure)
                return await FailBeforeReservationsAndFinalizeAsync(
                    idempotencyKey, order, attemptId,
                    policy.Error, cancellationToken);
            discountPolicy = policy.Value;
        }

        var plan = _fulfillmentPlanner.CreateBestPlan(
            demands, offers.Value, discountPolicy, _clock.UtcNow);
        if (plan.IsFailure)
            return await FailBeforeReservationsAndFinalizeAsync(
                idempotencyKey, order, attemptId,
                plan.Error, cancellationToken);

        var attached = order.AttachFulfillmentPlan(
            attemptId, plan.Value, _clock.UtcNow);
        if (attached.IsFailure)
            return await FailBeforeReservationsAndFinalizeAsync(
                idempotencyKey, order, attemptId,
                attached.Error, cancellationToken);
        var planSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (planSave.IsFailure)
        {
            await BestEffortFailBeforeReservationsAsync(
                orderId, attemptId, planSave.Error, cancellationToken);
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, planSave.Error, cancellationToken);
        }
        foreach (var vendor in plan.Value.Vendors
                     .OrderBy(vendor => vendor.VendorId.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationKey = ReservationOperationKey.For(
                order.Id, attemptId, vendor.VendorId);
            var intent = order.BeginInventoryReservation(
                attemptId,
                vendor.VendorId,
                operationKey,
                _clock.UtcNow);
            if (intent.IsFailure)
                return await CompensateAndFinalizeAsync(
                    idempotencyKey, order, attemptId,
                    intent.Error, cancellationToken);
            var intentSave = await _orderRepository.SaveAsync(
                order, cancellationToken);
            if (intentSave.IsFailure)
            {
                await BestEffortCompensatePersistedStateAsync(
                    orderId, attemptId, intentSave.Error, cancellationToken);
                return await FinalizeIdempotencyFailureAsync(
                    idempotencyKey, intentSave.Error, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var request = BuildReservationRequest(
                order.Id, attemptId, vendor, operationKey);
            var reserved = await _inventoryReservationService.ReserveAsync(
                request, cancellationToken);
            if (cancellationToken.IsCancellationRequested
                && reserved.IsSuccess
                && reserved.Value is InventoryReservationSucceeded
                    cancellationSuccess)
            {
                using var cleanupCancellation = new CancellationTokenSource(
                    PostReservationCleanupTimeout);
                await CleanupKnownSuccessAfterCancellationAsync(
                    order.Id,
                    attemptId,
                    vendor.VendorId,
                    operationKey,
                    cancellationSuccess.ReservationId,
                    cleanupCancellation.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (reserved.IsFailure)
                return Result<CheckoutOperationResult>.Failure(
                    IndeterminateReservationError(
                        order.Id, attemptId, vendor.VendorId,
                        operationKey, reserved.Error.Code));

            switch (reserved.Value)
            {
                case InventoryReservationIndeterminate indeterminate:
                    return Result<CheckoutOperationResult>.Failure(
                        IndeterminateReservationError(
                            order.Id, attemptId, vendor.VendorId,
                            operationKey, indeterminate.FailureCode));

                case InventoryReservationRejected rejected:
                {
                    var recorded = order.RecordInventoryReservationRejected(
                        attemptId,
                        operationKey,
                        rejected.FailureCode,
                        _clock.UtcNow);
                    if (recorded.IsFailure)
                        return await CompensateAndFinalizeAsync(
                            idempotencyKey, order, attemptId,
                            recorded.Error, cancellationToken);
                    var rejectionError = Error.DependencyFailure(
                        order.CheckoutAttempt!.Failure!.Code,
                        "Inventory rejected the Reservation.");
                    var rejectedSave = await _orderRepository.SaveAsync(
                        order, cancellationToken);
                    if (rejectedSave.IsFailure)
                        return await FinalizeIdempotencyFailureAsync(
                            idempotencyKey,
                            rejectedSave.Error,
                            cancellationToken);
                    return await ReleaseCompleteAndFinalizeAsync(
                        idempotencyKey,
                        order,
                        attemptId,
                        rejectionError,
                        cancellationToken);
                }

                case InventoryReservationSucceeded succeeded:
                {
                    var recorded = order.RecordInventoryReservationSucceeded(
                        attemptId,
                        operationKey,
                        succeeded.ReservationId,
                        succeeded.ReservedAt);
                    if (recorded.IsFailure)
                    {
                        return await CleanupUnpersistedReservationAsync(
                            idempotencyKey,
                            order,
                            attemptId,
                            vendor.VendorId,
                            operationKey,
                            succeeded.ReservationId,
                            recorded.Error,
                            cancellationToken);
                    }

                    var successSave = await _orderRepository.SaveAsync(
                        order, cancellationToken);
                    if (successSave.IsFailure)
                    {
                        return await CleanupUnpersistedReservationAsync(
                            idempotencyKey,
                            order,
                            attemptId,
                            vendor.VendorId,
                            operationKey,
                            succeeded.ReservationId,
                            successSave.Error,
                            cancellationToken);
                    }
                    break;
                }

                default:
                    return Result<CheckoutOperationResult>.Failure(
                        IndeterminateReservationError(
                            order.Id, attemptId, vendor.VendorId,
                            operationKey,
                            ApplicationErrors.DependencyOperationIndeterminate.Code));
            }
        }

        var completed = order.CompleteCheckout(attemptId, _clock.UtcNow);
        if (completed.IsFailure)
            return await CompensateAndFinalizeAsync(
                idempotencyKey, order, attemptId,
                completed.Error, cancellationToken);
        var completedSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (completedSave.IsFailure)
        {
            await BestEffortCompensatePersistedStateAsync(
                orderId, attemptId, completedSave.Error, cancellationToken);
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, completedSave.Error, cancellationToken);
        }
        var checkoutAttempt = order.CheckoutAttempt!;
        var result = new CheckoutOperationResult(
            order.Id,
            attemptId,
            order.Status,
            checkoutAttempt.FulfillmentPlan!.TotalPayable,
            checkoutAttempt.PaymentExpiresAt!.Value,
            order.Version);
        cancellationToken.ThrowIfCancellationRequested();
        var idempotencyCompletion = await _idempotencyStore.CompleteAsync(
            idempotencyKey, result, _clock.UtcNow, cancellationToken);
        return idempotencyCompletion.IsSuccess
            ? Result<CheckoutOperationResult>.Success(result)
            : Result<CheckoutOperationResult>.Failure(
                CheckoutApplicationErrors.IdempotencyFinalizationFailed(
                    Metadata(
                        ("orderId", order.Id),
                        ("checkoutAttemptId", attemptId),
                        ("orderVersion", order.Version),
                        ("originalErrorCode",
                            idempotencyCompletion.Error.Code))));
    }

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
                    Metadata(
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
        {
            var result = new CheckoutOperationResult(
                order.Id,
                claim.CheckoutAttemptId,
                order.Status,
                attempt.FulfillmentPlan.TotalPayable,
                attempt.PaymentExpiresAt.Value,
                order.Version);
            var repaired = await _idempotencyStore.CompleteAsync(
                idempotencyKey, result, _clock.UtcNow, cancellationToken);
            return repaired.IsSuccess
                ? Result<CheckoutOperationResult>.Success(result)
                : Result<CheckoutOperationResult>.Failure(
                    CheckoutApplicationErrors.IdempotencyFinalizationFailed(
                        Metadata(
                            ("orderId", order.Id),
                            ("checkoutAttemptId", claim.CheckoutAttemptId),
                            ("orderVersion", order.Version),
                            ("originalErrorCode", repaired.Error.Code))));
        }

        if (attempt?.Id == claim.CheckoutAttemptId
            && attempt.Status is CheckoutAttemptStatus.Failed
                or CheckoutAttemptStatus.CompensationPending
            && attempt.Failure is not null)
        {
            var error = RehydrateFailure(attempt.Failure.Code);
            var repaired = await _idempotencyStore.FailAsync(
                idempotencyKey, error, _clock.UtcNow, cancellationToken);
            return repaired.IsSuccess
                ? Result<CheckoutOperationResult>.Failure(error)
                : Result<CheckoutOperationResult>.Failure(
                    CheckoutApplicationErrors.IdempotencyFinalizationFailed(
                        Metadata(
                            ("orderId", order.Id),
                            ("checkoutAttemptId", claim.CheckoutAttemptId),
                            ("originalFailureCode", error.Code),
                            ("originalErrorCode", repaired.Error.Code))));
        }

        return Result<CheckoutOperationResult>.Failure(
            CheckoutApplicationErrors.IdempotencyInProgress(
                Metadata(
                    ("orderId", claim.OrderId),
                    ("checkoutAttemptId", claim.CheckoutAttemptId),
                    ("attemptStatus", attempt?.Status.ToString() ?? "missing"))));
    }

    private async Task<Result<CheckoutOperationResult>>
        FailBeforeReservationsAndFinalizeAsync(
            IdempotencyKey idempotencyKey,
            Order order,
            CheckoutAttemptId attemptId,
            Error originalError,
            CancellationToken cancellationToken)
    {
        var failed = await FailBeforeReservationsAsync(
            order, attemptId, originalError, cancellationToken);
        if (failed.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, failed.Error, cancellationToken);
        return await FinalizeIdempotencyFailureAsync(
            idempotencyKey, originalError, cancellationToken);
    }

    private async Task<Result<long>> FailBeforeReservationsAsync(
        Order order,
        CheckoutAttemptId attemptId,
        Error originalError,
        CancellationToken cancellationToken)
    {
        var failedAt = _clock.UtcNow;
        var failure = CheckoutFailure.Create(
            originalError.Code, failedAt);
        if (failure.IsFailure)
            return Result<long>.Failure(failure.Error);
        var failed = order.FailCheckoutBeforeReservations(
            attemptId, failure.Value, failedAt);
        if (failed.IsFailure)
            return Result<long>.Failure(failed.Error);
        return await _orderRepository.SaveAsync(
            order, cancellationToken);
    }

    private async Task<Result<CheckoutOperationResult>>
        CompensateAndFinalizeAsync(
            IdempotencyKey idempotencyKey,
            Order order,
            CheckoutAttemptId attemptId,
            Error originalError,
            CancellationToken cancellationToken)
    {
        var hasConfirmed = order.CheckoutAttempt!.Reservations.Any(
            reservation => reservation.Status is
                InventoryReservationStatus.Active
                or InventoryReservationStatus.ReleasePending);
        if (!hasConfirmed)
            return await FailBeforeReservationsAndFinalizeAsync(
                idempotencyKey, order, attemptId,
                originalError, cancellationToken);

        var failure = CheckoutFailure.Create(originalError.Code, _clock.UtcNow);
        if (failure.IsFailure)
            return Result<CheckoutOperationResult>.Failure(failure.Error);
        var begun = order.BeginCheckoutCompensation(
            attemptId, failure.Value);
        if (begun.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, begun.Error, cancellationToken);
        var compensatingSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (compensatingSave.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, compensatingSave.Error, cancellationToken);
        return await ReleaseCompleteAndFinalizeAsync(
            idempotencyKey,
            order,
            attemptId,
            originalError,
            cancellationToken);
    }

    private async Task<Result<CheckoutOperationResult>>
        ReleaseCompleteAndFinalizeAsync(
            IdempotencyKey idempotencyKey,
            Order order,
            CheckoutAttemptId attemptId,
            Error originalError,
            CancellationToken cancellationToken)
    {
        var released = await _releaseCoordinator
            .ReleaseForFailedCheckoutAsync(
                order, attemptId, cancellationToken);
        if (released.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, released.Error, cancellationToken);
        var completed = order.CompleteCheckoutFailure(
            attemptId, _clock.UtcNow);
        if (completed.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, completed.Error, cancellationToken);
        var finalSave = await _orderRepository.SaveAsync(
            order, cancellationToken);
        if (finalSave.IsFailure)
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, finalSave.Error, cancellationToken);
        return await FinalizeIdempotencyFailureAsync(
            idempotencyKey, originalError, cancellationToken);
    }

    private async Task<Result<CheckoutOperationResult>>
        CleanupUnpersistedReservationAsync(
            IdempotencyKey idempotencyKey,
            Order order,
            CheckoutAttemptId attemptId,
            VendorId vendorId,
            ReservationOperationKey operationKey,
            ReservationId reservationId,
            Error persistenceError,
            CancellationToken cancellationToken)
    {
        var released = await _inventoryReservationService.ReleaseAsync(
            new InventoryReleaseRequest(
                order.Id, attemptId, vendorId, reservationId),
            cancellationToken);
        if (released.IsSuccess
            && released.Value is InventoryReleaseSucceeded)
        {
            await BestEffortFailBeforeReservationsAsync(
                order.Id, attemptId, persistenceError, cancellationToken);
            return await FinalizeIdempotencyFailureAsync(
                idempotencyKey, persistenceError, cancellationToken);
        }

        var releaseErrorCode = released.IsFailure
            ? released.Error.Code
            : released.Value switch
            {
                InventoryReleaseFailed failure => failure.ErrorCode,
                InventoryReleaseIndeterminate indeterminate =>
                    indeterminate.ErrorCode,
                _ => ApplicationErrors.DependencyOperationIndeterminate.Code
            };
        var recovery = new ReservationRecoveryRecord(
            order.Id,
            attemptId,
            vendorId,
            operationKey,
            reservationId,
            releaseErrorCode,
            _clock.UtcNow,
            1);
        var recovered = await _reservationRecoveryStore.UpsertAsync(
            recovery, cancellationToken);
        if (recovered.IsFailure)
        {
            return Result<CheckoutOperationResult>.Failure(
                CheckoutApplicationErrors.RecoveryRecordFailed(
                    Metadata(
                        ("orderId", order.Id),
                        ("checkoutAttemptId", attemptId),
                        ("vendorId", vendorId),
                        ("operationKey", operationKey.Value),
                        ("reservationId", reservationId),
                        ("persistenceErrorCode", persistenceError.Code),
                        ("releaseErrorCode", releaseErrorCode),
                        ("recoveryErrorCode", recovered.Error.Code))));
        }

        await BestEffortFailBeforeReservationsAsync(
            order.Id, attemptId, persistenceError, cancellationToken);
        return await FinalizeIdempotencyFailureAsync(
            idempotencyKey, persistenceError, cancellationToken);
    }

    private async Task CleanupKnownSuccessAfterCancellationAsync(
        OrderId orderId,
        CheckoutAttemptId attemptId,
        VendorId vendorId,
        ReservationOperationKey operationKey,
        ReservationId reservationId,
        CancellationToken cancellationToken)
    {
        var released = await _inventoryReservationService.ReleaseAsync(
            new InventoryReleaseRequest(
                orderId, attemptId, vendorId, reservationId),
            cancellationToken);
        if (released.IsSuccess
            && released.Value is InventoryReleaseSucceeded)
            return;
        var errorCode = released.IsFailure
            ? released.Error.Code
            : released.Value switch
            {
                InventoryReleaseFailed failure => failure.ErrorCode,
                InventoryReleaseIndeterminate indeterminate =>
                    indeterminate.ErrorCode,
                _ => ApplicationErrors.DependencyOperationIndeterminate.Code
            };
        await _reservationRecoveryStore.UpsertAsync(
            new ReservationRecoveryRecord(
                orderId,
                attemptId,
                vendorId,
                operationKey,
                reservationId,
                errorCode,
                _clock.UtcNow,
                1),
            cancellationToken);
    }

    private async Task BestEffortFailBeforeReservationsAsync(
        OrderId orderId,
        CheckoutAttemptId attemptId,
        Error error,
        CancellationToken cancellationToken)
    {
        var reloaded = await _orderRepository.LoadAsync(
            orderId, cancellationToken);
        if (reloaded.IsFailure
            || reloaded.Value.CheckoutAttempt?.Id != attemptId
            || reloaded.Value.Status != OrderStatus.Processing)
            return;
        await FailBeforeReservationsAsync(
            reloaded.Value,
            attemptId,
            error,
            cancellationToken);
    }

    private async Task BestEffortCompensatePersistedStateAsync(
        OrderId orderId,
        CheckoutAttemptId attemptId,
        Error error,
        CancellationToken cancellationToken)
    {
        var reloaded = await _orderRepository.LoadAsync(
            orderId, cancellationToken);
        if (reloaded.IsFailure
            || reloaded.Value.CheckoutAttempt?.Id != attemptId)
            return;
        var persistedOrder = reloaded.Value;
        var active = persistedOrder.CheckoutAttempt.Reservations.Any(
            reservation => reservation.Status is
                InventoryReservationStatus.Active
                or InventoryReservationStatus.ReleasePending);
        if (!active)
        {
            await FailBeforeReservationsAsync(
                persistedOrder, attemptId,
                error, cancellationToken);
            return;
        }

        var failure = CheckoutFailure.Create(error.Code, _clock.UtcNow);
        if (failure.IsFailure
            || persistedOrder.BeginCheckoutCompensation(
                attemptId, failure.Value).IsFailure)
            return;
        var saved = await _orderRepository.SaveAsync(
            persistedOrder, cancellationToken);
        if (saved.IsFailure)
            return;
        var released = await _releaseCoordinator.ReleaseForFailedCheckoutAsync(
            persistedOrder, attemptId, cancellationToken);
        if (released.IsFailure)
            return;
        if (persistedOrder.CompleteCheckoutFailure(
                attemptId, _clock.UtcNow).IsFailure)
            return;
        await _orderRepository.SaveAsync(
            persistedOrder, cancellationToken);
    }

    private async Task<Result<CheckoutOperationResult>>
        FinalizeIdempotencyFailureAsync(
            IdempotencyKey idempotencyKey,
            Error originalError,
            CancellationToken cancellationToken)
    {
        var finalized = await _idempotencyStore.FailAsync(
            idempotencyKey,
            originalError,
            _clock.UtcNow,
            cancellationToken);
        return finalized.IsSuccess
            ? Result<CheckoutOperationResult>.Failure(originalError)
            : Result<CheckoutOperationResult>.Failure(
                CheckoutApplicationErrors.IdempotencyFinalizationFailed(
                    Metadata(
                        ("originalFailureCode", originalError.Code),
                        ("originalErrorCode", finalized.Error.Code))));
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
            orderId,
            attemptId,
            vendor.VendorId,
            operationKey,
            items);
    }

    private static Error IndeterminateReservationError(
        OrderId orderId,
        CheckoutAttemptId attemptId,
        VendorId vendorId,
        ReservationOperationKey operationKey,
        string dependencyErrorCode) =>
        CheckoutApplicationErrors.ReservationOutcomeIndeterminate(
            Metadata(
                ("orderId", orderId),
                ("checkoutAttemptId", attemptId),
                ("vendorId", vendorId),
                ("operationKey", operationKey.Value),
                ("dependencyErrorCode", dependencyErrorCode)));

    private static Error RehydrateFailure(string code)
    {
        Error[] knownErrors =
        [
            CheckoutErrors.NotAllowed,
            CheckoutErrors.AlreadyInProgress,
            CheckoutErrors.CompensationPending,
            CheckoutErrors.AttemptNotFound,
            CheckoutErrors.AttemptMismatch,
            CheckoutErrors.InvalidAttemptState,
            CheckoutErrors.PlanRequired,
            CheckoutErrors.PlanAlreadyAttached,
            CheckoutErrors.PlanDoesNotMatchOrder,
            CheckoutErrors.VendorNotInPlan,
            CheckoutErrors.InvalidReservationOperationKey,
            CheckoutErrors.ReservationAlreadyExists,
            CheckoutErrors.ReservationNotFound,
            CheckoutErrors.ReservationIdConflict,
            CheckoutErrors.ReservationInvalidState,
            CheckoutErrors.InvalidReservationExpiration,
            CheckoutErrors.ReservationsIncomplete,
            CheckoutErrors.ReservationExpired,
            CheckoutErrors.CompensationRequired,
            CheckoutErrors.CompensationNotComplete,
            CheckoutErrors.FailureRequired,
            ApplicationErrors.OrderNotFound,
            ApplicationErrors.OrderAlreadyExists,
            ApplicationErrors.OrderVersionConflict,
            ApplicationErrors.InvalidRequest,
            ApplicationErrors.DependencyOperationFailed,
            ApplicationErrors.DependencyOperationIndeterminate
        ];
        return knownErrors.FirstOrDefault(error => error.Code == code)
            ?? Error.DependencyFailure(code, "Checkout previously failed.");
    }

    private static IReadOnlyDictionary<string, string> Metadata(
        params (string Key, object Value)[] values) =>
        values.ToDictionary(
            value => value.Key,
            value => Convert.ToString(
                value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            StringComparer.Ordinal);
}

using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Payments;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders;

public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];
    private CheckoutAttempt? _checkoutAttempt;
    private PaymentRecord? _payment;
    private CancellationRecord? _cancellation;

    private Order(
        OrderId orderId,
        CustomerId customerId,
        DeliveryAddress deliveryAddress,
        IEnumerable<OrderItem> items,
        DateTimeOffset createdAt)
        : base(orderId)
    {
        CustomerId = customerId;
        DeliveryAddress = deliveryAddress;
        _items.AddRange(items);
        CreatedAt = createdAt;
        Status = OrderStatus.Draft;
    }

    public CustomerId CustomerId { get; }

    public DeliveryAddress DeliveryAddress { get; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyCollection<OrderItem> Items => _items.ToArray();

    public SelectedDiscountCode? SelectedDiscount { get; private set; }
    public CheckoutAttempt? CheckoutAttempt => _checkoutAttempt;
    public DateTimeOffset? PaymentExpiresAt => _checkoutAttempt?.PaymentExpiresAt;
    public PaymentRecord? Payment => _payment;
    public CancellationRecord? Cancellation => _cancellation;
    public DateTimeOffset? ExpiredAt { get; private set; }

    internal static Order Rehydrate(
        OrderId orderId,
        CustomerId customerId,
        DeliveryAddress deliveryAddress,
        IEnumerable<OrderItem> items,
        OrderStatus status,
        DateTimeOffset createdAt,
        SelectedDiscountCode? selectedDiscount,
        CheckoutAttempt? checkoutAttempt,
        PaymentRecord? payment,
        CancellationRecord? cancellation,
        DateTimeOffset? expiredAt)
    {
        var order = new Order(
            orderId, customerId, deliveryAddress, items, createdAt)
        {
            Status = status,
            SelectedDiscount = selectedDiscount,
            _checkoutAttempt = checkoutAttempt,
            _payment = payment,
            _cancellation = cancellation,
            ExpiredAt = expiredAt
        };
        return order;
    }

    public IReadOnlyCollection<ProductDemand> GetDemandSnapshot() =>
        _items.Select(item => new ProductDemand(
            new ProductReference(item.ProductId, item.ProductName), item.Quantity)).ToArray();

    public Result StartCheckout(CheckoutAttemptId attemptId, DateTimeOffset startedAt)
    {
        if (_checkoutAttempt?.Status == CheckoutAttemptStatus.CompensationPending)
            return Result.Failure(CheckoutErrors.CompensationPending);
        if (_checkoutAttempt?.Status is CheckoutAttemptStatus.Planning
            or CheckoutAttemptStatus.Reserving or CheckoutAttemptStatus.FullyReserved
            or CheckoutAttemptStatus.Compensating)
            return Result.Failure(CheckoutErrors.AlreadyInProgress);
        if (Status != OrderStatus.Draft) return Result.Failure(CheckoutErrors.NotAllowed);
        if (_items.Count == 0) return Result.Failure(OrderErrors.ItemsRequired);
        _checkoutAttempt = CheckoutAttempt.Create(attemptId, startedAt);
        Status = OrderStatus.Processing;
        RaiseDomainEvent(new OrderSubmittedForProcessingDomainEvent(Id, attemptId, startedAt));
        return Result.Success();
    }

    public Result AttachFulfillmentPlan(CheckoutAttemptId attemptId, FulfillmentPlan? plan, DateTimeOffset attachedAt)
    {
        var attemptResult = CurrentAttempt(attemptId, true);
        if (attemptResult.IsFailure) return Result.Failure(attemptResult.Error);
        if (plan is null) return Result.Failure(CheckoutErrors.PlanRequired);
        if (attemptResult.Value.FulfillmentPlan is not null)
            return Result.Failure(CheckoutErrors.PlanAlreadyAttached);
        if (!PlanMatchesOrder(plan)) return Result.Failure(CheckoutErrors.PlanDoesNotMatchOrder);
        var attach = attemptResult.Value.AttachPlan(plan);
        if (attach.IsFailure) return attach;
        RaiseDomainEvent(new FulfillmentPlanCreatedDomainEvent(
            Id, attemptId, plan.ProductsAmount, plan.DiscountAmount,
            plan.ShippingAmount, plan.TotalPayable, plan.VendorCount,
            plan.MaximumDeliveryHours, attachedAt));
        return Result.Success();
    }

    public Result BeginInventoryReservation(CheckoutAttemptId attemptId, VendorId vendorId, ReservationOperationKey operationKey, DateTimeOffset requestedAt)
    {
        var attemptResult = CurrentAttempt(attemptId, true);
        if (attemptResult.IsFailure) return Result.Failure(attemptResult.Error);
        var attempt = attemptResult.Value;
        if (attempt.Status != CheckoutAttemptStatus.Reserving || attempt.FulfillmentPlan is null)
            return Result.Failure(CheckoutErrors.InvalidAttemptState);
        if (!attempt.FulfillmentPlan.Vendors.Any(v => v.VendorId == vendorId))
            return Result.Failure(CheckoutErrors.VendorNotInPlan);
        var expected = ReservationOperationKey.For(Id, attemptId, vendorId);
        if (operationKey != expected) return Result.Failure(CheckoutErrors.InvalidReservationOperationKey);
        var existing = attempt.Reservations.FirstOrDefault(r => r.VendorId == vendorId);
        if (existing is not null)
            return existing.OperationKey == operationKey && existing.Status == InventoryReservationStatus.Pending
                ? Result.Success() : Result.Failure(CheckoutErrors.ReservationAlreadyExists);
        var reservation = InventoryReservation.CreatePending(vendorId, operationKey, requestedAt).Value;
        attempt.Add(reservation);
        RaiseDomainEvent(new InventoryReservationRequestedDomainEvent(Id, attemptId, vendorId, operationKey, requestedAt));
        return Result.Success();
    }

    public Result RecordInventoryReservationSucceeded(CheckoutAttemptId attemptId, ReservationOperationKey operationKey, ReservationId reservationId, DateTimeOffset reservedAt)
    {
        var attemptResult = CurrentAttempt(attemptId, true);
        if (attemptResult.IsFailure) return Result.Failure(attemptResult.Error);
        var reservation = attemptResult.Value.Find(operationKey);
        if (reservation is null) return Result.Failure(CheckoutErrors.ReservationNotFound);
        var active = reservation.MarkActive(reservationId, reservedAt);
        if (active.IsFailure) return Result.Failure(active.Error);
        if (active.Value)
            RaiseDomainEvent(new InventoryReservedDomainEvent(Id, attemptId, reservation.VendorId, reservationId, reservedAt, reservation.ExpiresAt!.Value));
        attemptResult.Value.RefreshReservationStatus();
        return Result.Success();
    }

    public Result RecordInventoryReservationRejected(CheckoutAttemptId attemptId, ReservationOperationKey operationKey, string failureCode, DateTimeOffset failedAt)
    {
        var attemptResult = CurrentAttempt(attemptId, true);
        if (attemptResult.IsFailure) return Result.Failure(attemptResult.Error);
        var reservation = attemptResult.Value.Find(operationKey);
        if (reservation is null) return Result.Failure(CheckoutErrors.ReservationNotFound);
        var failure = CheckoutFailure.Create(failureCode, failedAt);
        if (failure.IsFailure) return Result.Failure(failure.Error);
        if (attemptResult.Value.Failure is { } existingFailure
            && existingFailure.Code != failure.Value.Code)
            return Result.Failure(CheckoutErrors.InvalidAttemptState);
        var rejected = reservation.MarkRejected(failure.Value.Code);
        if (rejected.IsFailure) return Result.Failure(rejected.Error);
        var set = attemptResult.Value.SetFailure(failure.Value);
        if (set.IsFailure) return set;
        if (rejected.Value)
            RaiseDomainEvent(new InventoryReservationFailedDomainEvent(Id, attemptId, reservation.VendorId, operationKey, failure.Value.Code, failedAt));
        return Result.Success();
    }

    public Result BeginCheckoutCompensation(CheckoutAttemptId attemptId, CheckoutFailure? failure)
    {
        var attempt = CurrentAttempt(attemptId, true);
        if (attempt.IsFailure) return Result.Failure(attempt.Error);
        return failure is null
            ? Result.Failure(CheckoutErrors.FailureRequired)
            : attempt.Value.SetFailure(failure);
    }

    public Result MarkInventoryReservationReleased(CheckoutAttemptId attemptId, ReservationId reservationId, DateTimeOffset releasedAt)
    {
        var attempt = MatchingAttempt(attemptId);
        if (attempt.IsFailure) return Result.Failure(attempt.Error);
        if (Status != OrderStatus.Processing && !(Status == OrderStatus.Draft
            && attempt.Value.Status == CheckoutAttemptStatus.CompensationPending)
            && Status is not (OrderStatus.Cancelled or OrderStatus.Expired))
            return Result.Failure(CheckoutErrors.NotAllowed);
        var reservation = attempt.Value.Find(reservationId);
        if (reservation is null) return Result.Failure(CheckoutErrors.ReservationNotFound);
        var released = reservation.MarkReleased(releasedAt);
        if (released.IsFailure) return Result.Failure(released.Error);
        if (released.Value)
            RaiseDomainEvent(new InventoryReservationReleasedDomainEvent(Id, attemptId, reservation.VendorId, reservationId, releasedAt));
        return Result.Success();
    }

    public Result MarkInventoryReservationReleasePending(CheckoutAttemptId attemptId, ReservationId reservationId, string errorCode, DateTimeOffset attemptedAt)
    {
        var attempt = MatchingAttempt(attemptId);
        if (attempt.IsFailure) return Result.Failure(attempt.Error);
        if (Status != OrderStatus.Processing && !(Status == OrderStatus.Draft
            && attempt.Value.Status == CheckoutAttemptStatus.CompensationPending)
            && Status is not (OrderStatus.Cancelled or OrderStatus.Expired))
            return Result.Failure(CheckoutErrors.NotAllowed);
        var reservation = attempt.Value.Find(reservationId);
        if (reservation is null) return Result.Failure(CheckoutErrors.ReservationNotFound);
        var pending = reservation.MarkReleasePending(errorCode, attemptedAt);
        if (pending.IsFailure) return pending;
        RaiseDomainEvent(new InventoryReservationReleaseFailedDomainEvent(
            Id, attemptId, reservation.VendorId, reservationId,
            reservation.LastReleaseErrorCode!, reservation.ReleaseAttemptCount, attemptedAt));
        return Result.Success();
    }

    public Result CompleteCheckoutFailure(CheckoutAttemptId attemptId, DateTimeOffset failedAt)
    {
        if (Status == OrderStatus.Draft && _checkoutAttempt?.Id == attemptId
            && _checkoutAttempt.Status is CheckoutAttemptStatus.Failed or CheckoutAttemptStatus.CompensationPending)
            return Result.Success();
        var attempt = CurrentAttempt(attemptId, true);
        if (attempt.IsFailure) return Result.Failure(attempt.Error);
        if (attempt.Value.Status != CheckoutAttemptStatus.Compensating)
            return Result.Failure(CheckoutErrors.InvalidAttemptState);
        if (attempt.Value.Failure is null) return Result.Failure(CheckoutErrors.FailureRequired);
        if (attempt.Value.Reservations.Any(r => r.Status is InventoryReservationStatus.Pending or InventoryReservationStatus.Active))
            return Result.Failure(CheckoutErrors.CompensationNotComplete);
        var pending = attempt.Value.Reservations.Any(r => r.Status == InventoryReservationStatus.ReleasePending);
        attempt.Value.FinalizeFailure(pending); Status = OrderStatus.Draft;
        RaiseDomainEvent(new CheckoutFailedDomainEvent(Id, attemptId, attempt.Value.Failure.Code, pending, failedAt));
        return Result.Success();
    }

    public Result CompletePendingCompensation(CheckoutAttemptId attemptId)
    {
        var attempt = MatchingAttempt(attemptId);
        if (attempt.IsFailure) return Result.Failure(attempt.Error);
        if (Status != OrderStatus.Draft || attempt.Value.Status != CheckoutAttemptStatus.CompensationPending)
            return Result.Failure(CheckoutErrors.InvalidAttemptState);
        if (attempt.Value.Reservations.Any(r => r.Status is InventoryReservationStatus.Active or InventoryReservationStatus.ReleasePending))
            return Result.Failure(CheckoutErrors.CompensationNotComplete);
        attempt.Value.CompleteCompensation(); return Result.Success();
    }

    public Result FailCheckoutBeforeReservations(CheckoutAttemptId attemptId, CheckoutFailure? failure, DateTimeOffset failedAt)
    {
        if (failure is null) return Result.Failure(CheckoutErrors.FailureRequired);
        if (Status == OrderStatus.Draft && _checkoutAttempt?.Id == attemptId
            && _checkoutAttempt.Status == CheckoutAttemptStatus.Failed
            && _checkoutAttempt.Failure?.Code == failure.Code) return Result.Success();
        var attempt = CurrentAttempt(attemptId, true);
        if (attempt.IsFailure) return Result.Failure(attempt.Error);
        if (attempt.Value.Reservations.Any(r => r.Status is InventoryReservationStatus.Active or InventoryReservationStatus.ReleasePending))
            return Result.Failure(CheckoutErrors.CompensationRequired);
        var set = attempt.Value.SetFailure(failure); if (set.IsFailure) return set;
        attempt.Value.FinalizeFailure(false); Status = OrderStatus.Draft;
        RaiseDomainEvent(new CheckoutFailedDomainEvent(Id, attemptId, failure.Code, false, failedAt));
        return Result.Success();
    }

    public Result CompleteCheckout(CheckoutAttemptId attemptId, DateTimeOffset completedAt)
    {
        if (Status == OrderStatus.AwaitingPayment && _checkoutAttempt?.Id == attemptId
            && _checkoutAttempt.Status == CheckoutAttemptStatus.Completed) return Result.Success();
        var attempt = CurrentAttempt(attemptId, true);
        if (attempt.IsFailure) return Result.Failure(attempt.Error);
        if (attempt.Value.Status != CheckoutAttemptStatus.FullyReserved || attempt.Value.FulfillmentPlan is null)
            return Result.Failure(CheckoutErrors.ReservationsIncomplete);
        var reservations = attempt.Value.Reservations;
        if (reservations.Count != attempt.Value.FulfillmentPlan.VendorCount
            || reservations.Any(r => r.Status != InventoryReservationStatus.Active)
            || !reservations.Select(r => r.VendorId).ToHashSet().SetEquals(
                attempt.Value.FulfillmentPlan.Vendors.Select(v => v.VendorId)))
            return Result.Failure(CheckoutErrors.ReservationsIncomplete);
        if (reservations.Any(r => r.ExpiresAt <= completedAt))
            return Result.Failure(CheckoutErrors.ReservationExpired);
        var expiresAt = reservations.Min(r => r.ExpiresAt!.Value);
        attempt.Value.Complete(completedAt, expiresAt); Status = OrderStatus.AwaitingPayment;
        RaiseDomainEvent(new OrderAwaitingPaymentDomainEvent(Id, attemptId,
            attempt.Value.FulfillmentPlan.TotalPayable, expiresAt, completedAt));
        return Result.Success();
    }

    public Result ConfirmPayment(
        TransactionId transactionId,
        MarketplaceOrdering.Domain.Money.Money amount,
        DateTimeOffset paidAt)
    {
        if (Status == OrderStatus.Paid && _payment is not null)
        {
            return _payment.TransactionId == transactionId
                   && _payment.Amount == amount
                ? Result.Success()
                : Result.Failure(
                    PaymentErrors.AlreadyConfirmedWithDifferentData);
        }

        if (Status != OrderStatus.AwaitingPayment)
            return Result.Failure(PaymentErrors.NotAllowed);
        var attempt = _checkoutAttempt;
        if (attempt is null
            || attempt.Status != CheckoutAttemptStatus.Completed
            || attempt.FulfillmentPlan is null
            || !attempt.PaymentExpiresAt.HasValue)
            return Result.Failure(PaymentErrors.ReservationsInvalid);
        if (amount != attempt.FulfillmentPlan.TotalPayable)
            return Result.Failure(PaymentErrors.AmountMismatch);

        var reservations = attempt.Reservations;
        if (reservations.Count != attempt.FulfillmentPlan.VendorCount
            || reservations.Any(reservation =>
                reservation.Status != InventoryReservationStatus.Active
                || !reservation.ReservationId.HasValue
                || !reservation.ExpiresAt.HasValue)
            || !reservations.Select(reservation => reservation.VendorId)
                .ToHashSet()
                .SetEquals(attempt.FulfillmentPlan.Vendors.Select(
                    vendor => vendor.VendorId)))
            return Result.Failure(PaymentErrors.ReservationsInvalid);
        if (reservations.Any(reservation =>
                paidAt >= reservation.ExpiresAt!.Value))
            return Result.Failure(PaymentErrors.ReservationExpired);

        var payment = PaymentRecord.Create(transactionId, amount, paidAt);
        if (payment.IsFailure) return Result.Failure(payment.Error);
        _payment = payment.Value;
        Status = OrderStatus.Paid;
        RaiseDomainEvent(new OrderPaidDomainEvent(
            Id,
            attempt.Id,
            transactionId,
            amount,
            paidAt));
        return Result.Success();
    }

    public Result Cancel(
        CancellationReason reason,
        DateTimeOffset cancelledAt)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (Status == OrderStatus.Cancelled)
            return Result.Success();
        if (Status is not (OrderStatus.Draft
            or OrderStatus.Processing
            or OrderStatus.AwaitingPayment))
            return Result.Failure(CancellationErrors.NotAllowed);

        var previousStatus = Status;
        _cancellation = new CancellationRecord(
            reason, cancelledAt, previousStatus);
        Status = OrderStatus.Cancelled;
        var hasConfirmedReservations = _checkoutAttempt?.Reservations.Any(
            reservation => reservation.ReservationId.HasValue
                && reservation.Status is InventoryReservationStatus.Active
                    or InventoryReservationStatus.ReleasePending
                    or InventoryReservationStatus.Released) == true;
        RaiseDomainEvent(new OrderCancelledDomainEvent(
            Id,
            previousStatus,
            reason,
            cancelledAt,
            hasConfirmedReservations));
        return Result.Success();
    }

    public Result Expire(DateTimeOffset expiredAt)
    {
        if (Status == OrderStatus.Expired)
            return Result.Success();
        if (Status != OrderStatus.AwaitingPayment
            || _checkoutAttempt?.Status != CheckoutAttemptStatus.Completed)
            return Result.Failure(ExpirationErrors.NotAllowed);
        if (!_checkoutAttempt.PaymentExpiresAt.HasValue)
            return Result.Failure(
                ExpirationErrors.PaymentExpirationMissing);
        if (expiredAt < _checkoutAttempt.PaymentExpiresAt.Value)
            return Result.Failure(ExpirationErrors.NotDue);

        ExpiredAt = expiredAt;
        Status = OrderStatus.Expired;
        RaiseDomainEvent(new OrderExpiredDomainEvent(
            Id,
            _checkoutAttempt.Id,
            expiredAt,
            _checkoutAttempt.PaymentExpiresAt.Value));
        return Result.Success();
    }

    public static Result<Order> Create(
        OrderId orderId,
        CustomerId customerId,
        DeliveryAddress deliveryAddress,
        IReadOnlyCollection<InitialOrderItem>? initialItems,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(deliveryAddress);

        var itemsResult = CreateInitialItems(initialItems);
        if (itemsResult.IsFailure)
        {
            return Result<Order>.Failure(itemsResult.Error);
        }

        var order = new Order(
            orderId,
            customerId,
            deliveryAddress,
            itemsResult.Value,
            createdAt);

        order.RaiseDomainEvent(
            new OrderCreatedDomainEvent(orderId, customerId, createdAt));

        foreach (var item in order.Items)
        {
            order.RaiseDomainEvent(
                new OrderItemAddedDomainEvent(
                    orderId,
                    item.ProductId,
                    item.ProductName,
                    item.Quantity,
                    createdAt));
        }

        return Result<Order>.Success(order);
    }

    public Result AddItem(
        ProductReference product,
        Quantity quantity,
        DateTimeOffset occurredAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        var existingItem = FindItem(product.ProductId);
        if (existingItem is null)
        {
            var itemResult = OrderItem.Create(product, quantity);
            if (itemResult.IsFailure)
            {
                return Result.Failure(itemResult.Error);
            }

            _items.Add(itemResult.Value);
            RaiseDomainEvent(
                new OrderItemAddedDomainEvent(
                    Id,
                    itemResult.Value.ProductId,
                    itemResult.Value.ProductName,
                    itemResult.Value.Quantity,
                    occurredAt));

            return Result.Success();
        }

        var previousQuantity = existingItem.Quantity;
        var increaseResult = existingItem.IncreaseQuantity(quantity);
        if (increaseResult.IsFailure)
        {
            return increaseResult;
        }

        RaiseDomainEvent(
            new OrderItemQuantityIncreasedDomainEvent(
                Id,
                existingItem.ProductId,
                previousQuantity,
                quantity,
                existingItem.Quantity,
                occurredAt));

        return Result.Success();
    }

    public Result ChangeItemQuantity(
        ProductId productId,
        Quantity newQuantity,
        DateTimeOffset occurredAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        var item = FindItem(productId);
        if (item is null)
        {
            return Result.Failure(OrderErrors.ProductNotFound(productId));
        }

        var previousQuantity = item.Quantity;
        if (previousQuantity == newQuantity)
        {
            return Result.Success();
        }

        var changeResult = item.ChangeQuantity(newQuantity);
        if (changeResult.IsFailure)
        {
            return changeResult;
        }

        RaiseDomainEvent(
            new OrderItemQuantityChangedDomainEvent(
                Id,
                item.ProductId,
                previousQuantity,
                item.Quantity,
                occurredAt));

        return Result.Success();
    }

    public Result RemoveItem(
        ProductId productId,
        DateTimeOffset occurredAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        var item = FindItem(productId);
        if (item is null)
        {
            return Result.Failure(OrderErrors.ProductNotFound(productId));
        }

        if (_items.Count == 1)
        {
            return Result.Failure(OrderErrors.LastItemCannotBeRemoved);
        }

        _items.Remove(item);
        RaiseDomainEvent(
            new OrderItemRemovedDomainEvent(
                Id,
                item.ProductId,
                item.Quantity,
                occurredAt));

        return Result.Success();
    }

    public Result SelectDiscountCode(
        DiscountCode code,
        DateTimeOffset selectedAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        ArgumentNullException.ThrowIfNull(code);
        if (SelectedDiscount is { } selected && selected.Code == code)
        {
            return Result.Success();
        }

        SelectedDiscount = new SelectedDiscountCode(code, selectedAt);
        RaiseDomainEvent(
            new DiscountCodeSelectedDomainEvent(Id, code, selectedAt));

        return Result.Success();
    }

    public Result RemoveDiscountCode(DateTimeOffset removedAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        if (SelectedDiscount is not { } selected)
        {
            return Result.Success();
        }

        SelectedDiscount = null;
        RaiseDomainEvent(
            new DiscountCodeRemovedDomainEvent(Id, selected.Code, removedAt));

        return Result.Success();
    }

    private static Result<List<OrderItem>> CreateInitialItems(
        IReadOnlyCollection<InitialOrderItem>? initialItems)
    {
        if (initialItems is null || initialItems.Count == 0)
        {
            return Result<List<OrderItem>>.Failure(OrderErrors.ItemsRequired);
        }

        var items = new List<OrderItem>();
        foreach (var initialItem in initialItems)
        {
            var existingItem = items.FirstOrDefault(
                item => item.ProductId == initialItem.Product.ProductId);

            if (existingItem is not null)
            {
                var increaseResult = existingItem.IncreaseQuantity(initialItem.Quantity);
                if (increaseResult.IsFailure)
                {
                    return Result<List<OrderItem>>.Failure(increaseResult.Error);
                }

                continue;
            }

            var itemResult = OrderItem.Create(
                initialItem.Product,
                initialItem.Quantity);
            if (itemResult.IsFailure)
            {
                return Result<List<OrderItem>>.Failure(itemResult.Error);
            }

            items.Add(itemResult.Value);
        }

        return Result<List<OrderItem>>.Success(items);
    }

    private OrderItem? FindItem(ProductId productId) =>
        _items.FirstOrDefault(item => item.ProductId == productId);

    private Result<CheckoutAttempt> CurrentAttempt(
        CheckoutAttemptId attemptId,
        bool requireProcessing)
    {
        if (requireProcessing && Status != OrderStatus.Processing)
        {
            return Result<CheckoutAttempt>.Failure(CheckoutErrors.NotAllowed);
        }

        return MatchingAttempt(attemptId);
    }

    private Result<CheckoutAttempt> MatchingAttempt(CheckoutAttemptId attemptId)
    {
        if (_checkoutAttempt is null)
        {
            return Result<CheckoutAttempt>.Failure(CheckoutErrors.AttemptNotFound);
        }

        return _checkoutAttempt.Id == attemptId
            ? Result<CheckoutAttempt>.Success(_checkoutAttempt)
            : Result<CheckoutAttempt>.Failure(CheckoutErrors.AttemptMismatch);
    }

    private bool PlanMatchesOrder(FulfillmentPlan plan)
    {
        if (plan.VendorCount is < 1 or > 3)
        {
            return false;
        }

        var allocationsByProduct = plan.ProductAllocations
            .GroupBy(allocation => allocation.ProductId)
            .ToArray();

        if (allocationsByProduct.Length != _items.Count)
        {
            return false;
        }

        foreach (var item in _items)
        {
            var allocations = allocationsByProduct
                .SingleOrDefault(group => group.Key == item.ProductId);

            if (allocations is null
                || allocations.Sum(allocation => allocation.Quantity.Value) != item.Quantity.Value
                || allocations.Any(allocation => allocation.ProductName != item.ProductName)
                || allocations.Select(allocation => allocation.VendorId).Distinct().Count() > 2)
            {
                return false;
            }
        }

        return allocationsByProduct.All(group =>
            _items.Any(item => item.ProductId == group.Key));
    }

    private Result EnsureDraft() =>
        Status == OrderStatus.Draft
            ? Result.Success()
            : Result.Failure(OrderErrors.NotEditable);
}

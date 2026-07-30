using System.Collections.ObjectModel;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Inventory;

public sealed record InventoryReservationRequest
{
    private readonly ReadOnlyCollection<InventoryReservationItem> _items;

    public InventoryReservationRequest(
        OrderId orderId,
        CheckoutAttemptId checkoutAttemptId,
        VendorId vendorId,
        ReservationOperationKey operationKey,
        IReadOnlyCollection<InventoryReservationItem> items)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(items);
        OrderId = orderId;
        CheckoutAttemptId = checkoutAttemptId;
        VendorId = vendorId;
        OperationKey = operationKey;
        _items = Array.AsReadOnly(items.ToArray());
    }

    public OrderId OrderId { get; }
    public CheckoutAttemptId CheckoutAttemptId { get; }
    public VendorId VendorId { get; }
    public ReservationOperationKey OperationKey { get; }
    public IReadOnlyCollection<InventoryReservationItem> Items => _items;
}

public sealed record InventoryReservationItem(
    ProductId ProductId,
    Quantity Quantity);

public abstract record InventoryReservationOutcome;

public sealed record InventoryReservationSucceeded(
    ReservationId ReservationId,
    DateTimeOffset ReservedAt) : InventoryReservationOutcome;

public sealed record InventoryReservationRejected(
    string FailureCode) : InventoryReservationOutcome;

public sealed record InventoryReservationIndeterminate(
    string FailureCode) : InventoryReservationOutcome;

public sealed record InventoryReleaseRequest(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationId ReservationId);

public abstract record InventoryReleaseOutcome;

public sealed record InventoryReleaseSucceeded : InventoryReleaseOutcome;

public sealed record InventoryReleaseFailed(
    string ErrorCode) : InventoryReleaseOutcome;

public sealed record InventoryReleaseIndeterminate(
    string ErrorCode) : InventoryReleaseOutcome;

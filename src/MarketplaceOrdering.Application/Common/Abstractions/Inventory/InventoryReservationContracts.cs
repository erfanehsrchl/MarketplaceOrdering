using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Inventory;

public sealed record InventoryReservationRequest(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationOperationKey OperationKey,
    IReadOnlyList<InventoryReservationItem> Items);

public sealed record InventoryReservationItem(
    ProductId ProductId,
    Quantity Quantity);

/// <summary>
/// Read-only lookup of a previously attempted Reservation by its operation key.
/// </summary>
public sealed record InventoryReservationQuery(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationOperationKey OperationKey);

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

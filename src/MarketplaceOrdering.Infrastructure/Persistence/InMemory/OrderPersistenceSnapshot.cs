using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Money;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Infrastructure.Persistence.InMemory;

internal sealed record OrderPersistenceSnapshot(
    OrderId OrderId,
    long Version,
    CustomerId CustomerId,
    DeliveryAddress DeliveryAddress,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OrderItemSnapshot> Items,
    SelectedDiscountCode? SelectedDiscount,
    CheckoutAttemptSnapshot? CheckoutAttempt,
    PaymentSnapshot? Payment,
    CancellationSnapshot? Cancellation,
    DateTimeOffset? ExpiredAt);

internal sealed record OrderItemSnapshot(
    ProductId ProductId,
    ProductName ProductName,
    Quantity Quantity);

internal sealed record CheckoutAttemptSnapshot(
    CheckoutAttemptId Id,
    CheckoutAttemptStatus Status,
    DateTimeOffset StartedAt,
    FulfillmentPlan? FulfillmentPlan,
    IReadOnlyList<InventoryReservationSnapshot> Reservations,
    CheckoutFailure? Failure,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? PaymentExpiresAt);

internal sealed record InventoryReservationSnapshot(
    VendorId VendorId,
    ReservationOperationKey OperationKey,
    InventoryReservationStatus Status,
    DateTimeOffset RequestedAt,
    ReservationId? ReservationId,
    DateTimeOffset? ReservedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ReleasedAt,
    string? FailureCode,
    int ReleaseAttemptCount,
    string? LastReleaseErrorCode,
    DateTimeOffset? LastReleaseAttemptedAt);

internal sealed record PaymentSnapshot(
    TransactionId TransactionId,
    Money Amount,
    DateTimeOffset PaidAt);

internal sealed record CancellationSnapshot(
    CancellationReason Reason,
    DateTimeOffset CancelledAt,
    OrderStatus PreviousStatus);

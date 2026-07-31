namespace MarketplaceOrdering.Application.Orders.Models;

public sealed record OrderDetails(
    Guid OrderId,
    Guid CustomerId,
    string DeliveryAddress,
    string Status,
    DateTimeOffset CreatedAt,
    long Version,
    IReadOnlyList<OrderItemDetails> Items,
    SelectedDiscountDetails? SelectedDiscount,
    CheckoutAttemptSummary? CheckoutAttempt,
    PaymentDetails? Payment = null,
    CancellationDetails? Cancellation = null,
    DateTimeOffset? ExpiredAt = null,
    bool HasPendingReservationReleases = false);

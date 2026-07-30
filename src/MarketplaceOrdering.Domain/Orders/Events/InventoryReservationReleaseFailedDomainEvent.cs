using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record InventoryReservationReleaseFailedDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationId ReservationId,
    string ErrorCode,
    int AttemptCount,
    DateTimeOffset AttemptedAt) : DomainEvent(AttemptedAt);

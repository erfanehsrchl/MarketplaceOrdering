using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record InventoryReservationReleasedDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationId ReservationId,
    DateTimeOffset ReleasedAt) : DomainEvent(ReleasedAt);

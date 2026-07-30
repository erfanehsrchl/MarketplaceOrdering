using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record InventoryReservedDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationId ReservationId,
    DateTimeOffset ReservedAt,
    DateTimeOffset ExpiresAt) : DomainEvent(ReservedAt);

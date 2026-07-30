using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record InventoryReservationRequestedDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationOperationKey OperationKey,
    DateTimeOffset RequestedAt) : DomainEvent(RequestedAt);

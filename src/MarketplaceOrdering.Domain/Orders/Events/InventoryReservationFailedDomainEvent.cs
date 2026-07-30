using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record InventoryReservationFailedDomainEvent(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationOperationKey OperationKey,
    string FailureCode,
    DateTimeOffset At) : DomainEvent(At);

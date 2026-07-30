using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders.Events;

public sealed record OrderCancelledDomainEvent(
    OrderId OrderId,
    OrderStatus PreviousStatus,
    CancellationReason CancellationReason,
    DateTimeOffset CancelledAt,
    bool HasConfirmedReservations) : DomainEvent(CancelledAt);

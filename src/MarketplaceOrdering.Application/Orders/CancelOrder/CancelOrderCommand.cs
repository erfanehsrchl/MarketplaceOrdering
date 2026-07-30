namespace MarketplaceOrdering.Application.Orders.CancelOrder;

public sealed record CancelOrderCommand(Guid OrderId, string Reason);

public sealed record CancelOrderResult(
    Guid OrderId,
    string Status,
    string Reason,
    DateTimeOffset CancelledAt,
    bool HasPendingReservationReleases,
    long Version);

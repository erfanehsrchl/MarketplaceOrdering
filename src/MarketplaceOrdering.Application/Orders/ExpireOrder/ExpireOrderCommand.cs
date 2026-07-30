namespace MarketplaceOrdering.Application.Orders.ExpireOrder;

public sealed record ExpireOrderCommand(Guid OrderId);

public sealed record ExpireOrderResult(
    Guid OrderId,
    string Status,
    DateTimeOffset ExpiredAt,
    bool HasPendingReservationReleases,
    long Version);

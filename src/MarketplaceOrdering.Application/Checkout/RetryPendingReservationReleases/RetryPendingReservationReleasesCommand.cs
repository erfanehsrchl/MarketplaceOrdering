namespace MarketplaceOrdering.Application.Checkout.RetryPendingReservationReleases;

public sealed record RetryPendingReservationReleasesCommand(Guid OrderId);

public sealed record RetryPendingReservationReleasesResult(
    Guid OrderId,
    string OrderStatus,
    int RemainingPendingReleaseCount,
    long Version);

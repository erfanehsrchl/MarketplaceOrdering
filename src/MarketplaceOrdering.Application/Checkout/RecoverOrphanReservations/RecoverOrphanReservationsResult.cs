namespace MarketplaceOrdering.Application.Checkout.RecoverOrphanReservations;

public sealed record RecoverOrphanReservationsResult(
    int LoadedCount,
    int ReleasedCount,
    int FailedCount);

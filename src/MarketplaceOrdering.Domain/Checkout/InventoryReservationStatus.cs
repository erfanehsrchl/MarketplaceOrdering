namespace MarketplaceOrdering.Domain.Checkout;

public enum InventoryReservationStatus
{
    Pending = 1, Active = 2, Rejected = 3,
    ReleasePending = 4, Released = 5
}

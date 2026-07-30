namespace MarketplaceOrdering.Domain.Checkout;

public enum CheckoutAttemptStatus
{
    Planning = 1, Reserving = 2, FullyReserved = 3,
    Compensating = 4, CompensationPending = 5,
    Failed = 6, Completed = 7
}

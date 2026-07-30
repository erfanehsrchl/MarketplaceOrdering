namespace MarketplaceOrdering.Domain.Orders;

public enum OrderStatus
{
    Draft = 1,
    Processing = 2,
    AwaitingPayment = 3,
    Paid = 4,
    Cancelled = 5,
    Expired = 6
}

namespace MarketplaceOrdering.Application.Orders.Models;

public sealed record PaymentDetails(
    string TransactionId,
    long Amount,
    DateTimeOffset PaidAt);

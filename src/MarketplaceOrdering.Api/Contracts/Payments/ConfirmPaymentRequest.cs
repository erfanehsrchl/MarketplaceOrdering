namespace MarketplaceOrdering.Api.Contracts.Payments;

public sealed record ConfirmPaymentRequest(
    string TransactionId,
    long Amount,
    DateTimeOffset PaidAt);

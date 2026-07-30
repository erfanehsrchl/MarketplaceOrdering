namespace MarketplaceOrdering.Application.Payments.ConfirmPayment;

public sealed record ConfirmPaymentCommand(
    Guid OrderId,
    string TransactionId,
    long Amount,
    DateTimeOffset PaidAt);

public sealed record ConfirmPaymentResult(
    Guid OrderId,
    string Status,
    string TransactionId,
    long PaidAmount,
    DateTimeOffset PaidAt,
    long Version);

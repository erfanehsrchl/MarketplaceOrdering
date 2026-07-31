using MediatR;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Payments.ConfirmPayment;

public sealed record ConfirmPaymentCommand(
    Guid OrderId,
    string TransactionId,
    long Amount,
    DateTimeOffset PaidAt) : IRequest<Result<ConfirmPaymentResult>>;

public sealed record ConfirmPaymentResult(
    Guid OrderId,
    string Status,
    string TransactionId,
    long PaidAmount,
    DateTimeOffset PaidAt,
    long Version);

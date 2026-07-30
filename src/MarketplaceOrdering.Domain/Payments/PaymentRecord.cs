using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Payments;

public sealed class PaymentRecord
{
    private PaymentRecord(
        TransactionId transactionId,
        MoneyValue amount,
        DateTimeOffset paidAt)
    {
        TransactionId = transactionId;
        Amount = amount;
        PaidAt = paidAt;
    }

    public TransactionId TransactionId { get; }
    public MoneyValue Amount { get; }
    public DateTimeOffset PaidAt { get; }

    internal static Result<PaymentRecord> Create(
        TransactionId transactionId,
        MoneyValue amount,
        DateTimeOffset paidAt) =>
        amount.Amount <= 0
            ? Result<PaymentRecord>.Failure(PaymentErrors.AmountNotPositive)
            : Result<PaymentRecord>.Success(
                new PaymentRecord(transactionId, amount, paidAt));
}

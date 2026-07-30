using MarketplaceOrdering.Domain.Shared;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Discounts;

public sealed record FixedDiscountValue : DiscountValue
{
    private FixedDiscountValue(MoneyValue amount) => Amount = amount;

    public MoneyValue Amount { get; }

    public static Result<FixedDiscountValue> Create(MoneyValue amount) =>
        amount.Amount == 0
            ? Result<FixedDiscountValue>.Failure(
                DiscountErrors.FixedAmountNotPositive)
            : Result<FixedDiscountValue>.Success(
                new FixedDiscountValue(amount));
}

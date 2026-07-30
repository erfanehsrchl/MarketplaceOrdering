using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Discounts;

public sealed record PercentageDiscountValue : DiscountValue
{
    public const decimal MaximumPercentage = 30m;

    private PercentageDiscountValue(decimal percentage) =>
        Percentage = percentage;

    public decimal Percentage { get; }

    public static Result<PercentageDiscountValue> Create(decimal percentage)
    {
        if (percentage <= 0)
        {
            return Result<PercentageDiscountValue>.Failure(
                DiscountErrors.PercentageNotPositive);
        }

        return percentage > MaximumPercentage
            ? Result<PercentageDiscountValue>.Failure(
                DiscountErrors.PercentageExceedsMaximum)
            : Result<PercentageDiscountValue>.Success(
                new PercentageDiscountValue(percentage));
    }
}

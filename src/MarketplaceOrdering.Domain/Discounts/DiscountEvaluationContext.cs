using System.Collections.ObjectModel;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Discounts;

public sealed class DiscountEvaluationContext
{
    private readonly ReadOnlyCollection<VendorProductAmount> _vendorAmounts;

    private DiscountEvaluationContext(
        MoneyValue totalProductsAmount,
        IReadOnlyList<VendorProductAmount> vendorAmounts,
        DateTimeOffset evaluatedAt)
    {
        TotalProductsAmount = totalProductsAmount;
        _vendorAmounts = Array.AsReadOnly(vendorAmounts.ToArray());
        EvaluatedAt = evaluatedAt;
    }

    public MoneyValue TotalProductsAmount { get; }

    public IReadOnlyCollection<VendorProductAmount> VendorAmounts =>
        _vendorAmounts;

    public DateTimeOffset EvaluatedAt { get; }

    public static Result<DiscountEvaluationContext> Create(
        MoneyValue totalProductsAmount,
        IReadOnlyCollection<VendorProductAmount>? vendorAmounts,
        DateTimeOffset evaluatedAt)
    {
        if (vendorAmounts is null || vendorAmounts.Count == 0)
        {
            return Result<DiscountEvaluationContext>.Failure(
                DiscountErrors.VendorAmountsRequired);
        }

        var orderedAmounts = vendorAmounts
            .OrderBy(amount => amount.VendorId.Value)
            .ToArray();

        var seenVendorIds = new HashSet<VendorId>();
        var sum = MoneyValue.Zero;
        foreach (var vendorAmount in orderedAmounts)
        {
            if (!seenVendorIds.Add(vendorAmount.VendorId))
            {
                return Result<DiscountEvaluationContext>.Failure(
                    DiscountErrors.DuplicateVendor(vendorAmount.VendorId));
            }

            var sumResult = sum.Add(vendorAmount.ProductsAmount);
            if (sumResult.IsFailure)
            {
                return Result<DiscountEvaluationContext>.Failure(
                    DiscountErrors.CalculationOverflow);
            }

            sum = sumResult.Value;
        }

        if (sum != totalProductsAmount)
        {
            return Result<DiscountEvaluationContext>.Failure(
                DiscountErrors.InconsistentTotalProductsAmount(
                    totalProductsAmount.Amount,
                    sum.Amount));
        }

        return Result<DiscountEvaluationContext>.Success(
            new DiscountEvaluationContext(
                totalProductsAmount,
                orderedAmounts,
                evaluatedAt));
    }
}

using System.Collections.ObjectModel;
using System.Numerics;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Discounts;

public sealed class DiscountCalculation
{
    private readonly ReadOnlyCollection<VendorDiscountAllocation> _vendorAllocations;

    private DiscountCalculation(
        DiscountCode code,
        DiscountValue appliedValue,
        MoneyValue totalProductsAmount,
        MoneyValue eligibleProductsAmount,
        MoneyValue totalDiscountAmount,
        IReadOnlyCollection<VendorDiscountAllocation> vendorAllocations,
        DateTimeOffset evaluatedAt)
    {
        Code = code;
        AppliedValue = appliedValue;
        TotalProductsAmount = totalProductsAmount;
        EligibleProductsAmount = eligibleProductsAmount;
        TotalDiscountAmount = totalDiscountAmount;
        _vendorAllocations = Array.AsReadOnly(
            vendorAllocations
                .OrderBy(allocation => allocation.VendorId.Value)
                .ToArray());
        EvaluatedAt = evaluatedAt;
    }

    public DiscountCode Code { get; }

    public DiscountValue AppliedValue { get; }

    public MoneyValue TotalProductsAmount { get; }

    public MoneyValue EligibleProductsAmount { get; }

    public MoneyValue TotalDiscountAmount { get; }

    public IReadOnlyCollection<VendorDiscountAllocation> VendorAllocations =>
        _vendorAllocations;

    public DateTimeOffset EvaluatedAt { get; }

    internal static Result<DiscountCalculation> Create(
        DiscountCode code,
        DiscountValue appliedValue,
        MoneyValue totalProductsAmount,
        MoneyValue eligibleProductsAmount,
        MoneyValue totalDiscountAmount,
        IReadOnlyCollection<VendorDiscountAllocation> vendorAllocations,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(appliedValue);
        ArgumentNullException.ThrowIfNull(vendorAllocations);

        if (totalDiscountAmount.Amount == 0 && vendorAllocations.Count != 0)
        {
            return Result<DiscountCalculation>.Failure(
                DiscountErrors.AllocationFailed);
        }

        var allocationSum = vendorAllocations.Aggregate(
            BigInteger.Zero,
            (sum, allocation) => sum + allocation.DiscountAmount.Amount);

        if (allocationSum != totalDiscountAmount.Amount
            || vendorAllocations.Any(
                allocation => allocation.DiscountAmount.Amount <= 0))
        {
            return Result<DiscountCalculation>.Failure(
                DiscountErrors.AllocationFailed);
        }

        return Result<DiscountCalculation>.Success(
            new DiscountCalculation(
                code,
                appliedValue,
                totalProductsAmount,
                eligibleProductsAmount,
                totalDiscountAmount,
                vendorAllocations,
                evaluatedAt));
    }
}

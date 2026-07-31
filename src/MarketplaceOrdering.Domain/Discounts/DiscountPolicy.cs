using System.Collections.ObjectModel;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Discounts;

public sealed class DiscountPolicy
{
    private readonly ReadOnlyCollection<VendorId> _eligibleVendorIds;

    private DiscountPolicy(
        DiscountCode code,
        DiscountValue value,
        bool isActive,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        MoneyValue? minimumProductsAmount,
        MoneyValue? maximumDiscountAmount,
        IReadOnlyCollection<VendorId> eligibleVendorIds)
    {
        Code = code;
        Value = value;
        IsActive = isActive;
        StartsAt = startsAt;
        EndsAt = endsAt;
        MinimumProductsAmount = minimumProductsAmount;
        MaximumDiscountAmount = maximumDiscountAmount;
        _eligibleVendorIds = Array.AsReadOnly(eligibleVendorIds.ToArray());
    }

    public DiscountCode Code { get; }

    public DiscountValue Value { get; }

    public bool IsActive { get; }

    public DateTimeOffset? StartsAt { get; }

    public DateTimeOffset? EndsAt { get; }

    public MoneyValue? MinimumProductsAmount { get; }

    public MoneyValue? MaximumDiscountAmount { get; }

    public IReadOnlyCollection<VendorId> EligibleVendorIds =>
        _eligibleVendorIds;

    public static Result<DiscountPolicy> Create(
        DiscountCode? code,
        DiscountValue? value,
        bool isActive,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        MoneyValue? minimumProductsAmount = null,
        MoneyValue? maximumDiscountAmount = null,
        IReadOnlyCollection<VendorId>? eligibleVendorIds = null)
    {
        if (code is null)
        {
            return Result<DiscountPolicy>.Failure(DiscountErrors.CodeRequired);
        }

        if (value is null)
        {
            return Result<DiscountPolicy>.Failure(DiscountErrors.ValueRequired);
        }

        if (startsAt.HasValue
            && endsAt.HasValue
            && startsAt.Value > endsAt.Value)
        {
            return Result<DiscountPolicy>.Failure(
                DiscountErrors.InvalidDateRange);
        }

        if (maximumDiscountAmount is { Amount: 0 })
        {
            return Result<DiscountPolicy>.Failure(
                DiscountErrors.MaximumAmountNotPositive);
        }

        var normalizedVendorIds = (eligibleVendorIds ?? Array.Empty<VendorId>())
            .Distinct()
            .OrderBy(vendorId => vendorId.Value)
            .ToArray();

        return Result<DiscountPolicy>.Success(
            new DiscountPolicy(
                code,
                value,
                isActive,
                startsAt,
                endsAt,
                minimumProductsAmount,
                maximumDiscountAmount,
                normalizedVendorIds));
    }

    /// <summary>
    /// Validates everything that can be known before any price exists, so a
    /// structurally unusable code is rejected while the Order is still a Draft
    /// instead of surfacing only at Checkout.
    /// </summary>
    /// <remarks>
    /// Amount-dependent conditions (<see cref="MinimumProductsAmount"/>,
    /// <see cref="MaximumDiscountAmount"/>) and Vendor eligibility deliberately
    /// stay out of this check: in Draft the Order has no prices and no Vendors
    /// yet, because both are resolved from Vendor Offers during Checkout. They
    /// are enforced by <see cref="Evaluate"/> against the selected
    /// Fulfillment Plan.
    /// </remarks>
    public Result EnsureSelectableAt(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return Result.Failure(DiscountErrors.Inactive);
        }

        if (StartsAt.HasValue && now < StartsAt.Value)
        {
            return Result.Failure(DiscountErrors.NotStarted);
        }

        return EndsAt.HasValue && now > EndsAt.Value
            ? Result.Failure(DiscountErrors.Expired)
            : Result.Success();
    }

    public Result<DiscountCalculation> Evaluate(
        DiscountEvaluationContext context,
        ProportionalDiscountAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(allocator);

        var applicabilityResult = EnsureApplicable(context);
        if (applicabilityResult.IsFailure)
        {
            return Result<DiscountCalculation>.Failure(
                applicabilityResult.Error);
        }

        var eligibleVendorAmounts = GetEligibleVendorAmounts(context);
        if (eligibleVendorAmounts.Count == 0)
        {
            return Result<DiscountCalculation>.Failure(
                DiscountErrors.NotApplicable);
        }

        var eligibleAmountResult = SumEligibleAmount(eligibleVendorAmounts);
        if (eligibleAmountResult.IsFailure)
        {
            return Result<DiscountCalculation>.Failure(
                eligibleAmountResult.Error);
        }

        var eligibleProductsAmount = eligibleAmountResult.Value;
        var rawDiscountResult = CalculateRawDiscount(eligibleProductsAmount);
        if (rawDiscountResult.IsFailure)
        {
            return Result<DiscountCalculation>.Failure(
                rawDiscountResult.Error);
        }

        var finalDiscountAmount = ApplyCaps(
            rawDiscountResult.Value,
            eligibleProductsAmount);

        var allocationResult = allocator.Allocate(
            finalDiscountAmount,
            eligibleVendorAmounts);
        if (allocationResult.IsFailure)
        {
            return Result<DiscountCalculation>.Failure(
                allocationResult.Error);
        }

        return DiscountCalculation.Create(
            Code,
            Value,
            context.TotalProductsAmount,
            eligibleProductsAmount,
            finalDiscountAmount,
            allocationResult.Value,
            context.EvaluatedAt);
    }

    private Result EnsureApplicable(DiscountEvaluationContext context)
    {
        var selectable = EnsureSelectableAt(context.EvaluatedAt);
        if (selectable.IsFailure)
        {
            return selectable;
        }

        if (MinimumProductsAmount is { } minimum
            && context.TotalProductsAmount.Amount < minimum.Amount)
        {
            return Result.Failure(
                DiscountErrors.MinimumAmountNotMet(
                    minimum.Amount,
                    context.TotalProductsAmount.Amount));
        }

        return Result.Success();
    }

    private IReadOnlyCollection<VendorProductAmount> GetEligibleVendorAmounts(
        DiscountEvaluationContext context)
    {
        if (_eligibleVendorIds.Count == 0)
        {
            return context.VendorAmounts.ToArray();
        }

        var eligibleVendorIds = _eligibleVendorIds.ToHashSet();
        return context.VendorAmounts
            .Where(amount => eligibleVendorIds.Contains(amount.VendorId))
            .ToArray();
    }

    private static Result<MoneyValue> SumEligibleAmount(
        IEnumerable<VendorProductAmount> vendorAmounts)
    {
        var total = MoneyValue.Zero;
        foreach (var vendorAmount in vendorAmounts)
        {
            var addResult = total.Add(vendorAmount.ProductsAmount);
            if (addResult.IsFailure)
            {
                return Result<MoneyValue>.Failure(
                    DiscountErrors.CalculationOverflow);
            }

            total = addResult.Value;
        }

        return Result<MoneyValue>.Success(total);
    }

    private Result<MoneyValue> CalculateRawDiscount(MoneyValue eligibleProductsAmount)
    {
        if (Value is FixedDiscountValue fixedValue)
        {
            return Result<MoneyValue>.Success(fixedValue.Amount);
        }

        if (Value is not PercentageDiscountValue percentageValue)
        {
            return Result<MoneyValue>.Failure(DiscountErrors.ValueRequired);
        }

        try
        {
            var rawDiscount =
                eligibleProductsAmount.Amount
                * percentageValue.Percentage
                / 100m;
            var roundedDiscount = Math.Round(
                rawDiscount,
                0,
                MidpointRounding.ToEven);
            var amount = checked((long)roundedDiscount);
            return MoneyValue.Create(amount);
        }
        catch (OverflowException)
        {
            return Result<MoneyValue>.Failure(
                DiscountErrors.CalculationOverflow);
        }
    }

    private MoneyValue ApplyCaps(
        MoneyValue rawDiscount,
        MoneyValue eligibleProductsAmount)
    {
        var finalAmount = Math.Min(
            rawDiscount.Amount,
            eligibleProductsAmount.Amount);

        if (MaximumDiscountAmount is { } maximum)
        {
            finalAmount = Math.Min(finalAmount, maximum.Amount);
        }

        return MoneyValue.Create(finalAmount).Value;
    }
}

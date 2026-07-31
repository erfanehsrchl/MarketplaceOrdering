using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Fulfillment;

/// <summary>
/// One way of splitting a single Product across the Vendors of a subset,
/// pre-costed so the search never recomputes it.
/// </summary>
internal sealed class SubsetOption
{
    internal required ProductAllocation[] Allocations { get; init; }

    /// <summary>Product money this option adds, Shipping excluded.</summary>
    internal required long Cost { get; init; }

    /// <summary>What each subset Vendor receives, by subset position.</summary>
    internal required long[] VendorAmounts { get; init; }

    /// <summary>Which subset Vendors this option uses, as a bit set.</summary>
    internal required int VendorMask { get; init; }
}

/// <summary>
/// Everything the search needs to know about one Vendor subset, computed once
/// before any branching.
/// </summary>
/// <remarks>
/// Fixing the subset up front is what makes the bounds possible: Shipping and
/// delivery time stop depending on the allocation, and the cheapest and dearest
/// completion of the remaining Products can be tabulated as plain suffix sums.
/// </remarks>
internal sealed class SubsetContext
{
    private SubsetContext(
        VendorId[] vendors,
        Dictionary<VendorId, ProductOffer> terms,
        SubsetOption[][] options,
        long[] suffixMinimumCost,
        long[][] suffixMaximumVendorAmount,
        long[] minimumOrderAmounts,
        long shippingAmount,
        long maximumDiscount,
        int maxVendorsPerProduct)
    {
        Vendors = vendors;
        Terms = terms;
        Options = options;
        SuffixMinimumCost = suffixMinimumCost;
        SuffixMaximumVendorAmount = suffixMaximumVendorAmount;
        MinimumOrderAmounts = minimumOrderAmounts;
        ShippingAmount = shippingAmount;
        MaximumDiscount = maximumDiscount;
        MaxVendorsPerProduct = maxVendorsPerProduct;
        FullMask = (1 << vendors.Length) - 1;
    }

    internal VendorId[] Vendors { get; }
    internal Dictionary<VendorId, ProductOffer> Terms { get; }
    internal SubsetOption[][] Options { get; }

    /// <summary>Cheapest completion of Products <c>i..n-1</c>, indexed by i.</summary>
    internal long[] SuffixMinimumCost { get; }

    /// <summary>
    /// Largest amount each Vendor could still receive from Products
    /// <c>i..n-1</c>. Used to abandon branches where a Vendor can no longer
    /// reach its minimum order amount.
    /// </summary>
    internal long[][] SuffixMaximumVendorAmount { get; }

    internal long[] MinimumOrderAmounts { get; }
    internal long ShippingAmount { get; }

    /// <summary>
    /// Upper bound on the discount any completion inside this subset can earn.
    /// Deliberately generous: an over-estimate only weakens pruning, while an
    /// under-estimate could discard the optimal plan.
    /// </summary>
    internal long MaximumDiscount { get; }

    internal int MaxVendorsPerProduct { get; }
    internal int VendorCount => Vendors.Length;
    internal int FullMask { get; }

    /// <summary>
    /// Builds the context, or returns <c>null</c> when this subset cannot cover
    /// every Product and is therefore not worth branching on.
    /// </summary>
    internal static SubsetContext? TryCreate(
        VendorId[] vendors,
        ProductDemand[] demands,
        ProductOffer[] offers,
        Dictionary<VendorId, ProductOffer> termsByVendor,
        ProductAllocationGenerator generator,
        DiscountPolicy? policy)
    {
        var vendorPosition = new Dictionary<VendorId, int>(vendors.Length);
        for (var index = 0; index < vendors.Length; index++)
            vendorPosition[vendors[index]] = index;

        var subsetOffers = offers
            .Where(offer => vendorPosition.ContainsKey(offer.VendorId))
            .ToArray();

        var options = new SubsetOption[demands.Length][];
        var maximumProductsAmount = 0L;
        for (var index = 0; index < demands.Length; index++)
        {
            var generated = generator.Generate(demands[index], subsetOffers);
            if (generated.IsFailure || generated.Value.Count == 0) return null;

            var built = generated.Value
                .Select(option => Build(option, vendorPosition, vendors.Length))
                .OrderBy(option => option.Cost)
                .ThenBy(option => option.VendorMask)
                .ToArray();
            options[index] = built;
            maximumProductsAmount = AddSaturating(
                maximumProductsAmount, built.Max(option => option.Cost));
        }

        var productCount = demands.Length;
        var suffixMinimumCost = new long[productCount + 1];
        var suffixMaximumVendorAmount = new long[productCount + 1][];
        suffixMaximumVendorAmount[productCount] = new long[vendors.Length];
        for (var index = productCount - 1; index >= 0; index--)
        {
            suffixMinimumCost[index] = AddSaturating(
                suffixMinimumCost[index + 1],
                options[index].Min(option => option.Cost));
            var maxima = new long[vendors.Length];
            for (var vendor = 0; vendor < vendors.Length; vendor++)
                maxima[vendor] = AddSaturating(
                    suffixMaximumVendorAmount[index + 1][vendor],
                    options[index].Max(option => option.VendorAmounts[vendor]));
            suffixMaximumVendorAmount[index] = maxima;
        }

        var shipping = 0L;
        var minimums = new long[vendors.Length];
        for (var index = 0; index < vendors.Length; index++)
        {
            var terms = termsByVendor[vendors[index]];
            shipping = AddSaturating(shipping, terms.ShippingCost.Amount);
            minimums[index] = terms.MinimumOrderAmount.Amount;
        }

        return new SubsetContext(
            vendors,
            termsByVendor,
            options,
            suffixMinimumCost,
            suffixMaximumVendorAmount,
            minimums,
            shipping,
            UpperBoundDiscount(policy, maximumProductsAmount),
            MaxVendorsPerProductOf(options));
    }

    private static SubsetOption Build(
        ProductAllocationOption option,
        Dictionary<VendorId, int> vendorPosition,
        int vendorCount)
    {
        var allocations = option.Allocations.ToArray();
        var amounts = new long[vendorCount];
        var cost = 0L;
        var mask = 0;
        foreach (var allocation in allocations)
        {
            var position = vendorPosition[allocation.VendorId];
            amounts[position] = AddSaturating(
                amounts[position], allocation.LineTotal.Amount);
            cost = AddSaturating(cost, allocation.LineTotal.Amount);
            mask |= 1 << position;
        }

        return new SubsetOption
        {
            Allocations = allocations,
            Cost = cost,
            VendorAmounts = amounts,
            VendorMask = mask
        };
    }

    private static int MaxVendorsPerProductOf(SubsetOption[][] options) =>
        options.Max(product => product.Max(option => option.Allocations.Length));

    private static long UpperBoundDiscount(
        DiscountPolicy? policy,
        long maximumProductsAmount)
    {
        if (policy is null) return 0;
        var bound = policy.Value switch
        {
            FixedDiscountValue fixedValue =>
                Math.Min(fixedValue.Amount.Amount, maximumProductsAmount),
            PercentageDiscountValue percentage => PercentageBound(
                maximumProductsAmount, percentage.Percentage),
            _ => maximumProductsAmount
        };
        return policy.MaximumDiscountAmount is { } maximum
            ? Math.Min(bound, maximum.Amount)
            : bound;
    }

    private static long PercentageBound(long amount, decimal percentage)
    {
        try
        {
            return (long)Math.Ceiling(amount / 100m * percentage);
        }
        catch (OverflowException)
        {
            return amount;
        }
    }

    /// <summary>
    /// Saturating addition. Every value built here is a bound, and clamping a
    /// bound at <see cref="long.MaxValue"/> only ever makes pruning weaker, so
    /// an overflow can cost search time but never a correct plan. The genuine
    /// overflow is still reported later, by the exact money arithmetic that
    /// scores the candidate.
    /// </summary>
    private static long AddSaturating(long left, long right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }
}

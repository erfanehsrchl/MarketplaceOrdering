using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Fulfillment;

/// <summary>
/// Chooses the cheapest complete way to source an Order from Vendor Offers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why subsets.</b> "At most three Vendors per Order" is the constraint that
/// makes an exact search affordable. Enumerating Vendor subsets first — rather
/// than enumerating allocations and discovering the Vendor count at the end —
/// fixes two things per subset that are otherwise recomputed everywhere: total
/// Shipping (each Vendor is charged exactly once) and delivery time (the slowest
/// Vendor). Both then become constants the search can plan against.
/// </para>
/// <para>
/// <b>Why the discount is inside the search.</b> The ranking rule is lowest
/// <c>TotalPayable</c>, and the discount is part of that number. Picking the
/// cheapest gross plan and applying the discount afterwards is wrong whenever a
/// policy is restricted to some Vendors or carries a minimum-amount threshold,
/// because then the discount is not a monotone function of the gross amount.
/// Every candidate is therefore scored with the real discount calculation.
/// </para>
/// <para>
/// <b>Pruning.</b> Three cuts, none of which can remove an optimal or tying
/// plan: a fail-fast stock check before any search; a branch-and-bound cut using
/// the cheapest possible completion of the remaining Products, which only drops
/// branches whose best case is <i>strictly</i> worse than the incumbent, so ties
/// survive to be ranked; and a Vendor minimum-order feasibility cut that drops
/// branches where a Vendor can no longer reach its minimum even if it received
/// the most expensive share of every remaining Product.
/// </para>
/// <para>
/// <b>Determinism.</b> Demands, Offers, subsets, and options are all placed in a
/// fixed order derived from identifiers, and ranking ends in a total order (see
/// <see cref="FulfillmentPlanComparer"/>). Input order, hashing, and timing
/// cannot influence the result; traversal order affects only how early pruning
/// starts working.
/// </para>
/// </remarks>
public sealed class FulfillmentPlanner
{
    private readonly ProportionalDiscountAllocator _discountAllocator;
    private readonly ProductAllocationGenerator _allocationGenerator = new();

    public FulfillmentPlanner(ProportionalDiscountAllocator discountAllocator)
    {
        ArgumentNullException.ThrowIfNull(discountAllocator);
        _discountAllocator = discountAllocator;
    }

    public Result<FulfillmentPlan> CreateBestPlan(
        IReadOnlyCollection<ProductDemand>? demands,
        IReadOnlyCollection<ProductOffer>? offers,
        DiscountPolicy? discountPolicy,
        DateTimeOffset evaluatedAt) =>
        CreateBestPlan(
            demands, offers, discountPolicy, evaluatedAt,
            FulfillmentPlannerOptions.Default);

    public Result<FulfillmentPlan> CreateBestPlan(
        IReadOnlyCollection<ProductDemand>? demands,
        IReadOnlyCollection<ProductOffer>? offers,
        DiscountPolicy? discountPolicy,
        DateTimeOffset evaluatedAt,
        FulfillmentPlannerOptions? options)
    {
        var settings = options ?? FulfillmentPlannerOptions.Default;
        var demandResult = NormalizeDemands(demands);
        if (demandResult.IsFailure)
            return Result<FulfillmentPlan>.Failure(demandResult.Error);

        var offerResult = NormalizeOffers(offers, demandResult.Value);
        if (offerResult.IsFailure)
            return Result<FulfillmentPlan>.Failure(offerResult.Error);

        var search = new SubsetSearch(
            demandResult.Value,
            offerResult.Value,
            discountPolicy,
            evaluatedAt,
            settings,
            _allocationGenerator,
            _discountAllocator);
        return search.Run();
    }

    private static Result<ProductDemand[]> NormalizeDemands(
        IReadOnlyCollection<ProductDemand>? demands)
    {
        if (demands is null || demands.Count == 0)
            return Result<ProductDemand[]>.Failure(FulfillmentErrors.DemandsRequired);
        if (demands.Select(demand => demand.Product.ProductId).Distinct().Count()
            != demands.Count)
            return Result<ProductDemand[]>.Failure(
                FulfillmentErrors.DuplicateProductDemand);
        return Result<ProductDemand[]>.Success(demands
            .OrderBy(demand => demand.Product.ProductId.Value).ToArray());
    }

    private static Result<ProductOffer[]> NormalizeOffers(
        IReadOnlyCollection<ProductOffer>? offers,
        IReadOnlyCollection<ProductDemand> demands)
    {
        var productIds = demands.Select(demand => demand.Product.ProductId).ToHashSet();
        var usable = (offers ?? Array.Empty<ProductOffer>())
            .Where(offer => productIds.Contains(offer.ProductId)
                && offer.UnitPrice.Amount > 0 && offer.AvailableQuantity > 0)
            .OrderBy(offer => offer.VendorId.Value)
            .ThenBy(offer => offer.ProductId.Value).ToArray();
        if (usable.GroupBy(offer => (offer.VendorId, offer.ProductId))
            .Any(group => group.Count() > 1))
            return Result<ProductOffer[]>.Failure(FulfillmentErrors.DuplicateOffer);
        if (usable.GroupBy(offer => offer.VendorId).Any(group =>
                group.Select(offer => offer.ShippingCost).Distinct().Count() > 1
                || group.Select(offer => offer.MinimumOrderAmount)
                    .Distinct().Count() > 1))
            return Result<ProductOffer[]>.Failure(
                FulfillmentErrors.InconsistentVendorTerms);
        return Result<ProductOffer[]>.Success(usable);
    }

    /// <summary>
    /// One exact search over Vendor subsets. Holds the mutable search state that
    /// would otherwise have to be threaded through every recursive call.
    /// </summary>
    private sealed class SubsetSearch
    {
        private readonly ProductDemand[] _demands;
        private readonly ProductOffer[] _offers;
        private readonly DiscountPolicy? _policy;
        private readonly DateTimeOffset _evaluatedAt;
        private readonly FulfillmentPlannerOptions _options;
        private readonly ProductAllocationGenerator _generator;
        private readonly ProportionalDiscountAllocator _allocator;
        private readonly VendorId[] _vendors;
        private readonly Dictionary<VendorId, ProductOffer> _termsByVendor;

        private int _remainingBudget;
        private FulfillmentCandidate? _best;
        private FulfillmentCandidate? _bestRejectedByDiscount;
        private Error? _calculationError;

        internal SubsetSearch(
            ProductDemand[] demands,
            ProductOffer[] offers,
            DiscountPolicy? policy,
            DateTimeOffset evaluatedAt,
            FulfillmentPlannerOptions options,
            ProductAllocationGenerator generator,
            ProportionalDiscountAllocator allocator)
        {
            _demands = demands;
            _offers = offers;
            _policy = policy;
            _evaluatedAt = evaluatedAt;
            _options = options;
            _generator = generator;
            _allocator = allocator;
            _remainingBudget = options.MaxSearchNodes;
            _vendors = offers.Select(offer => offer.VendorId)
                .Distinct().OrderBy(vendorId => vendorId.Value).ToArray();
            _termsByVendor = offers
                .GroupBy(offer => offer.VendorId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        internal Result<FulfillmentPlan> Run()
        {
            if (!EveryProductCanPossiblyBeCovered())
                return Result<FulfillmentPlan>.Failure(FulfillmentErrors.NoValidPlan);

            // Smaller subsets first: a one-Vendor plan is both the cheapest to
            // evaluate and, at equal money, the preferred answer, so finding one
            // early gives every later subset something sharp to be pruned by.
            for (var size = 1;
                 size <= Math.Min(_options.MaxVendorsPerOrder, _vendors.Length);
                 size++)
            {
                var subset = new int[size];
                if (!ExploreSubsets(subset, 0, 0, size))
                    return Result<FulfillmentPlan>.Failure(
                        FulfillmentErrors.SearchBudgetExceeded);
            }

            if (_best is not null)
                return Result<FulfillmentPlan>.Success(new FulfillmentPlan(_best));
            if (_bestRejectedByDiscount is not null)
                return Result<FulfillmentPlan>.Failure(
                    _bestRejectedByDiscount.DiscountError!);
            return Result<FulfillmentPlan>.Failure(
                _calculationError ?? FulfillmentErrors.NoValidPlan);
        }

        /// <summary>
        /// A Product that cannot be covered even by its most stocked permitted
        /// Vendors makes the whole Order impossible, so this is settled before
        /// any subset is built.
        /// </summary>
        private bool EveryProductCanPossiblyBeCovered()
        {
            foreach (var demand in _demands)
            {
                var reachable = _offers
                    .Where(offer => offer.ProductId == demand.Product.ProductId)
                    .Select(offer => (long)offer.AvailableQuantity)
                    .OrderByDescending(quantity => quantity)
                    .Take(_options.MaxVendorsPerProduct)
                    .Sum();
                if (reachable < demand.Quantity.Value) return false;
            }

            return true;
        }

        /// <summary>Returns false when the search budget ran out.</summary>
        private bool ExploreSubsets(
            int[] subset, int position, int startIndex, int size)
        {
            if (position == size) return EvaluateSubset(subset);
            for (var index = startIndex;
                 index <= _vendors.Length - (size - position);
                 index++)
            {
                subset[position] = index;
                if (!ExploreSubsets(subset, position + 1, index + 1, size))
                    return false;
            }

            return true;
        }

        private bool EvaluateSubset(int[] subsetIndexes)
        {
            if (--_remainingBudget < 0) return false;
            var vendors = subsetIndexes.Select(index => _vendors[index]).ToArray();
            var context = SubsetContext.TryCreate(
                vendors, _demands, _offers, _termsByVendor, _generator, _policy);
            if (context is null) return true;

            // Best case for the whole subset: cheapest completion of every
            // Product, its fixed Shipping, minus the largest discount any
            // completion inside this subset could earn.
            if (_best is not null
                && context.SuffixMinimumCost[0]
                    + context.ShippingAmount
                    - context.MaximumDiscount > _best.TotalPayable.Amount)
                return true;

            var vendorAmounts = new long[vendors.Length];
            var selected = new List<ProductAllocation>();
            return Branch(context, 0, 0, vendorAmounts, 0, selected);
        }

        private bool Branch(
            SubsetContext context,
            int productIndex,
            long costSoFar,
            long[] vendorAmounts,
            int usedMask,
            List<ProductAllocation> selected)
        {
            if (--_remainingBudget < 0) return false;

            if (productIndex == context.Options.Length)
            {
                // Every Vendor in the subset must actually be used; otherwise
                // this is a smaller subset's plan, already enumerated on its own
                // and with less Shipping.
                if (usedMask != context.FullMask) return true;
                Consider(context, selected);
                return true;
            }

            var options = context.Options[productIndex];
            for (var index = 0; index < options.Length; index++)
            {
                var option = options[index];
                var newCost = costSoFar + option.Cost;

                // Cheapest-first ordering means once one option's best case is
                // too expensive, so is every later option for this Product.
                if (_best is not null
                    && newCost
                        + context.SuffixMinimumCost[productIndex + 1]
                        + context.ShippingAmount
                        - context.MaximumDiscount > _best.TotalPayable.Amount)
                    break;

                var newMask = usedMask | option.VendorMask;
                if (!CanStillUseEveryVendor(context, productIndex, newMask))
                    continue;

                for (var vendor = 0; vendor < vendorAmounts.Length; vendor++)
                    vendorAmounts[vendor] += option.VendorAmounts[vendor];

                if (CanStillReachEveryMinimum(context, productIndex, vendorAmounts))
                {
                    selected.AddRange(option.Allocations);
                    var withinBudget = Branch(
                        context, productIndex + 1, newCost,
                        vendorAmounts, newMask, selected);
                    selected.RemoveRange(
                        selected.Count - option.Allocations.Length,
                        option.Allocations.Length);
                    if (!withinBudget)
                    {
                        for (var vendor = 0; vendor < vendorAmounts.Length; vendor++)
                            vendorAmounts[vendor] -= option.VendorAmounts[vendor];
                        return false;
                    }
                }

                for (var vendor = 0; vendor < vendorAmounts.Length; vendor++)
                    vendorAmounts[vendor] -= option.VendorAmounts[vendor];
            }

            return true;
        }

        private static bool CanStillUseEveryVendor(
            SubsetContext context, int productIndex, int mask)
        {
            var remainingProducts = context.Options.Length - productIndex - 1;
            var missing = context.VendorCount - System.Numerics.BitOperations
                .PopCount((uint)mask);
            return missing <= remainingProducts * context.MaxVendorsPerProduct;
        }

        private static bool CanStillReachEveryMinimum(
            SubsetContext context, int productIndex, long[] vendorAmounts)
        {
            for (var vendor = 0; vendor < vendorAmounts.Length; vendor++)
            {
                if (vendorAmounts[vendor]
                    + context.SuffixMaximumVendorAmount[productIndex + 1][vendor]
                    < context.MinimumOrderAmounts[vendor])
                    return false;
            }

            return true;
        }

        private void Consider(
            SubsetContext context, List<ProductAllocation> selected)
        {
            var candidate = BuildCandidate(context, selected);
            if (candidate.IsFailure)
            {
                if (candidate.Error.Code == FulfillmentErrors.CalculationOverflow.Code)
                    _calculationError ??= candidate.Error;
                return;
            }

            if (candidate.Value.DiscountError is not null)
            {
                if (_bestRejectedByDiscount is null
                    || FulfillmentPlanComparer.Instance.CompareAllocationKey(
                        candidate.Value, _bestRejectedByDiscount) < 0)
                    _bestRejectedByDiscount = candidate.Value;
                return;
            }

            if (_best is null
                || FulfillmentPlanComparer.Instance.Compare(
                    candidate.Value, _best) < 0)
                _best = candidate.Value;
        }

        private Result<FulfillmentCandidate> BuildCandidate(
            SubsetContext context,
            IReadOnlyCollection<ProductAllocation> allocations)
        {
            var groups = allocations.GroupBy(allocation => allocation.VendorId)
                .OrderBy(group => group.Key.Value).ToArray();
            var vendorData = new List<VendorBuildData>();
            var productsAmount = MoneyValue.Zero;
            var shippingAmount = MoneyValue.Zero;

            foreach (var group in groups)
            {
                var vendorAllocations = group.ToArray();
                var terms = context.Terms[group.Key];
                var subtotalResult = SumMoney(
                    vendorAllocations.Select(allocation => allocation.LineTotal));
                if (subtotalResult.IsFailure)
                    return Result<FulfillmentCandidate>.Failure(subtotalResult.Error);
                if (subtotalResult.Value.Amount < terms.MinimumOrderAmount.Amount)
                    return Result<FulfillmentCandidate>.Failure(
                        FulfillmentErrors.NoValidPlan);

                var productAdd = productsAmount.Add(subtotalResult.Value);
                var shippingAdd = shippingAmount.Add(terms.ShippingCost);
                if (productAdd.IsFailure || shippingAdd.IsFailure)
                    return Result<FulfillmentCandidate>.Failure(
                        FulfillmentErrors.CalculationOverflow);
                productsAmount = productAdd.Value;
                shippingAmount = shippingAdd.Value;
                vendorData.Add(new VendorBuildData(
                    group.Key, vendorAllocations, subtotalResult.Value,
                    terms.ShippingCost, terms.MinimumOrderAmount));
            }

            DiscountCalculation? calculation = null;
            Error? discountError = null;
            var discountAmount = MoneyValue.Zero;
            if (_policy is not null)
            {
                var evaluationContext = DiscountEvaluationContext.Create(
                    productsAmount,
                    vendorData.Select(data => new VendorProductAmount(
                        data.VendorId, data.ProductsAmount)).ToArray(),
                    _evaluatedAt);
                if (evaluationContext.IsFailure)
                    return Result<FulfillmentCandidate>.Failure(
                        FulfillmentErrors.CalculationOverflow);
                var evaluation = _policy.Evaluate(
                    evaluationContext.Value, _allocator);
                if (evaluation.IsFailure)
                    discountError = evaluation.Error;
                else
                {
                    calculation = evaluation.Value;
                    discountAmount = calculation.TotalDiscountAmount;
                }
            }

            var discountByVendor = calculation?.VendorAllocations.ToDictionary(
                allocation => allocation.VendorId,
                allocation => allocation.DiscountAmount)
                ?? new Dictionary<VendorId, MoneyValue>();
            var vendors = new List<VendorFulfillment>();
            foreach (var data in vendorData)
            {
                var vendorResult = VendorFulfillment.Create(
                    data.VendorId, data.Allocations, data.ProductsAmount,
                    discountByVendor.GetValueOrDefault(data.VendorId, MoneyValue.Zero),
                    data.ShippingCost, data.MinimumOrderAmount);
                if (vendorResult.IsFailure)
                    return Result<FulfillmentCandidate>.Failure(vendorResult.Error);
                vendors.Add(vendorResult.Value);
            }

            var afterDiscount = productsAmount.Subtract(discountAmount);
            if (afterDiscount.IsFailure)
                return Result<FulfillmentCandidate>.Failure(
                    FulfillmentErrors.InvalidAllocation);
            var payable = afterDiscount.Value.Add(shippingAmount);
            if (payable.IsFailure)
                return Result<FulfillmentCandidate>.Failure(
                    FulfillmentErrors.CalculationOverflow);

            return Result<FulfillmentCandidate>.Success(new FulfillmentCandidate(
                allocations, vendors, productsAmount, discountAmount,
                shippingAmount, payable.Value, calculation,
                vendors.Max(vendor => vendor.EstimatedDeliveryHours),
                discountError));
        }

        private static Result<MoneyValue> SumMoney(IEnumerable<MoneyValue> values)
        {
            var total = MoneyValue.Zero;
            foreach (var value in values)
            {
                var result = total.Add(value);
                if (result.IsFailure)
                    return Result<MoneyValue>.Failure(
                        FulfillmentErrors.CalculationOverflow);
                total = result.Value;
            }
            return Result<MoneyValue>.Success(total);
        }
    }

    private sealed record VendorBuildData(
        VendorId VendorId,
        IReadOnlyCollection<ProductAllocation> Allocations,
        MoneyValue ProductsAmount,
        MoneyValue ShippingCost,
        MoneyValue MinimumOrderAmount);
}

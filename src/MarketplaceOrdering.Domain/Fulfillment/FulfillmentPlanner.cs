using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Fulfillment;

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
        DateTimeOffset evaluatedAt)
    {
        var demandResult = NormalizeDemands(demands);
        if (demandResult.IsFailure)
            return Result<FulfillmentPlan>.Failure(demandResult.Error);

        var offerResult = NormalizeOffers(offers, demandResult.Value);
        if (offerResult.IsFailure)
            return Result<FulfillmentPlan>.Failure(offerResult.Error);

        var optionSets = new List<IReadOnlyCollection<ProductAllocationOption>>();
        foreach (var demand in demandResult.Value)
        {
            var optionsResult = _allocationGenerator.Generate(
                demand, offerResult.Value);
            if (optionsResult.IsFailure)
                return Result<FulfillmentPlan>.Failure(optionsResult.Error);
            if (optionsResult.Value.Count == 0)
                return Result<FulfillmentPlan>.Failure(FulfillmentErrors.NoValidPlan);
            optionSets.Add(optionsResult.Value);
        }

        var candidates = new List<FulfillmentCandidate>();
        Error? calculationError = null;
        Backtrack(
            0, optionSets, [], offerResult.Value, discountPolicy, evaluatedAt,
            candidates, ref calculationError);

        if (candidates.Count == 0)
        {
            return Result<FulfillmentPlan>.Failure(
                calculationError ?? FulfillmentErrors.NoValidPlan);
        }

        var applicable = candidates
            .Where(candidate => candidate.DiscountError is null)
            .OrderBy(candidate => candidate, FulfillmentPlanComparer.Instance)
            .ToArray();
        if (applicable.Length > 0)
            return Result<FulfillmentPlan>.Success(new FulfillmentPlan(applicable[0]));

        var deterministicFailure = candidates.ToArray();
        Array.Sort(deterministicFailure, (left, right) =>
            FulfillmentPlanComparer.Instance.CompareAllocationKey(left, right));
        return Result<FulfillmentPlan>.Failure(
            deterministicFailure[0].DiscountError!);
    }

    private void Backtrack(
        int index,
        IReadOnlyList<IReadOnlyCollection<ProductAllocationOption>> optionSets,
        List<ProductAllocation> selected,
        IReadOnlyCollection<ProductOffer> offers,
        DiscountPolicy? policy,
        DateTimeOffset evaluatedAt,
        List<FulfillmentCandidate> candidates,
        ref Error? calculationError)
    {
        if (index == optionSets.Count)
        {
            var candidate = BuildCandidate(selected, offers, policy, evaluatedAt);
            if (candidate.IsSuccess)
                candidates.Add(candidate.Value);
            else if (candidate.Error.Code == FulfillmentErrors.CalculationOverflow.Code)
                calculationError ??= candidate.Error;
            return;
        }

        foreach (var option in optionSets[index])
        {
            selected.AddRange(option.Allocations);
            if (selected.Select(allocation => allocation.VendorId)
                    .Distinct().Count() <= 3)
            {
                Backtrack(index + 1, optionSets, selected, offers, policy,
                    evaluatedAt, candidates, ref calculationError);
            }
            selected.RemoveRange(
                selected.Count - option.Allocations.Count,
                option.Allocations.Count);
        }
    }

    private Result<FulfillmentCandidate> BuildCandidate(
        IReadOnlyCollection<ProductAllocation> allocations,
        IReadOnlyCollection<ProductOffer> offers,
        DiscountPolicy? policy,
        DateTimeOffset evaluatedAt)
    {
        var groups = allocations.GroupBy(allocation => allocation.VendorId)
            .OrderBy(group => group.Key.Value).ToArray();
        var vendorData = new List<VendorBuildData>();
        var productsAmount = MoneyValue.Zero;
        var shippingAmount = MoneyValue.Zero;

        foreach (var group in groups)
        {
            var vendorAllocations = group.ToArray();
            var terms = offers.First(offer => offer.VendorId == group.Key);
            var subtotalResult = SumMoney(
                vendorAllocations.Select(allocation => allocation.LineTotal));
            if (subtotalResult.IsFailure)
                return Result<FulfillmentCandidate>.Failure(subtotalResult.Error);
            if (subtotalResult.Value.Amount < terms.MinimumOrderAmount.Amount)
                return Result<FulfillmentCandidate>.Failure(FulfillmentErrors.NoValidPlan);

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
        if (policy is not null)
        {
            var context = DiscountEvaluationContext.Create(
                productsAmount,
                vendorData.Select(data => new VendorProductAmount(
                    data.VendorId, data.ProductsAmount)).ToArray(),
                evaluatedAt);
            if (context.IsFailure)
                return Result<FulfillmentCandidate>.Failure(
                    FulfillmentErrors.CalculationOverflow);
            var evaluation = policy.Evaluate(context.Value, _discountAllocator);
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

    private sealed record VendorBuildData(
        VendorId VendorId,
        IReadOnlyCollection<ProductAllocation> Allocations,
        MoneyValue ProductsAmount,
        MoneyValue ShippingCost,
        MoneyValue MinimumOrderAmount);
}

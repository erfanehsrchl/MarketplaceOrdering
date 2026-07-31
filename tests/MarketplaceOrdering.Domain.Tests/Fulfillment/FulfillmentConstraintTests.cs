using FluentAssertions;
using MarketplaceOrdering.Domain.Fulfillment;

namespace MarketplaceOrdering.Domain.Tests.Fulfillment;

/// <summary>
/// The allocation constraints that decide whether a combination is valid at all,
/// each asserted where it is the single reason a plan is rejected.
/// </summary>
public sealed class FulfillmentConstraintTests
{
    private static readonly DateTimeOffset EvaluatedAt =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The cheapest Vendor is unusable because the Order would not reach its
    /// minimum, so a more expensive Vendor that clears its own minimum wins.
    /// </summary>
    [Fact]
    public void VendorBelowItsMinimumOrderAmount_ShouldBeRejected()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1)],
            [
                FulfillmentTestData.Offer(1, 1, 50, 1, 0, 500),
                FulfillmentTestData.Offer(2, 1, 90, 1, 0, 90)
            ],
            null,
            EvaluatedAt).Value;

        plan.Vendors.Single().VendorId.Should()
            .Be(FulfillmentTestData.Vendor(2));
        plan.ProductsAmount.Amount.Should().Be(90);
    }

    /// <summary>
    /// When no Vendor can clear its minimum, there is no plan at all — partial
    /// fulfillment is never an answer.
    /// </summary>
    [Fact]
    public void NoVendorReachingItsMinimum_ShouldFailTheWholeCheckout()
    {
        var result = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1)],
            [
                FulfillmentTestData.Offer(1, 1, 50, 1, 0, 500),
                FulfillmentTestData.Offer(2, 1, 60, 1, 0, 600)
            ],
            null,
            EvaluatedAt);

        result.Error.Should().Be(FulfillmentErrors.NoValidPlan);
    }

    /// <summary>
    /// A minimum may be reached by combining several Products from the same
    /// Vendor; it is a Vendor-level rule, not a per-line one.
    /// </summary>
    [Fact]
    public void MinimumReachedAcrossSeveralProducts_ShouldBeAccepted()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [
                FulfillmentTestData.Demand(1, 1),
                FulfillmentTestData.Demand(2, 1)
            ],
            [
                FulfillmentTestData.Offer(1, 1, 60, 1, 0, 100),
                FulfillmentTestData.Offer(1, 2, 40, 1, 0, 100)
            ],
            null,
            EvaluatedAt).Value;

        plan.ProductsAmount.Amount.Should().Be(100);
        plan.VendorCount.Should().Be(1);
    }

    /// <summary>
    /// Shipping is charged once per Vendor and is outside the minimum-order
    /// check, so a Vendor whose products barely miss the minimum stays invalid
    /// even when Shipping would push the total over it.
    /// </summary>
    [Fact]
    public void ShippingShouldNotCountTowardsTheMinimumOrderAmount()
    {
        var result = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1)],
            [FulfillmentTestData.Offer(1, 1, 90, 1, 50, 100)],
            null,
            EvaluatedAt);

        result.Error.Should().Be(FulfillmentErrors.NoValidPlan);
    }

    /// <summary>
    /// One Product may use at most two Vendors, so a quantity that only three
    /// Vendors together could stock is unfulfillable.
    /// </summary>
    [Fact]
    public void ProductNeedingThreeVendors_ShouldBeRejected()
    {
        var result = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 3)],
            [
                FulfillmentTestData.Offer(1, 1, 10, 1),
                FulfillmentTestData.Offer(2, 1, 10, 1),
                FulfillmentTestData.Offer(3, 1, 10, 1)
            ],
            null,
            EvaluatedAt);

        result.Error.Should().Be(FulfillmentErrors.NoValidPlan);
    }

    /// <summary>
    /// Two Vendors are allowed for one Product, and the split is used when it is
    /// the only way to cover the quantity.
    /// </summary>
    [Fact]
    public void ProductSplitAcrossTwoVendors_ShouldBeAccepted()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 3)],
            [
                FulfillmentTestData.Offer(1, 1, 10, 2),
                FulfillmentTestData.Offer(2, 1, 10, 2)
            ],
            null,
            EvaluatedAt).Value;

        plan.ProductAllocations.Should().HaveCount(2);
        plan.ProductAllocations.Sum(allocation => allocation.Quantity.Value)
            .Should().Be(3);
        plan.VendorCount.Should().Be(2);
    }

    /// <summary>
    /// The whole Order may use at most three Vendors, so four Products that each
    /// exist at exactly one distinct Vendor cannot be fulfilled together.
    /// </summary>
    [Fact]
    public void OrderNeedingFourVendors_ShouldBeRejected()
    {
        var result = FulfillmentTestData.Planner().CreateBestPlan(
            Enumerable.Range(1, 4)
                .Select(number => FulfillmentTestData.Demand(number, 1))
                .ToArray(),
            Enumerable.Range(1, 4)
                .Select(number => FulfillmentTestData.Offer(
                    number, number, 10, 1))
                .ToArray(),
            null,
            EvaluatedAt);

        result.Error.Should().Be(FulfillmentErrors.NoValidPlan);
    }

    [Fact]
    public void ExactlyThreeVendors_ShouldBeAccepted()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            Enumerable.Range(1, 3)
                .Select(number => FulfillmentTestData.Demand(number, 1))
                .ToArray(),
            Enumerable.Range(1, 3)
                .Select(number => FulfillmentTestData.Offer(
                    number, number, 10, 1))
                .ToArray(),
            null,
            EvaluatedAt).Value;

        plan.VendorCount.Should().Be(3);
    }

    /// <summary>
    /// Offers priced at zero or with no stock are not merely unattractive, they
    /// are invalid and must not appear in any plan.
    /// </summary>
    [Fact]
    public void ZeroPriceAndZeroStockOffers_ShouldBeIgnored()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1)],
            [
                FulfillmentTestData.Offer(1, 1, 0, 5),
                FulfillmentTestData.Offer(2, 1, 10, 0),
                FulfillmentTestData.Offer(3, 1, 70, 1)
            ],
            null,
            EvaluatedAt).Value;

        plan.Vendors.Single().VendorId.Should()
            .Be(FulfillmentTestData.Vendor(3));
        plan.ProductsAmount.Amount.Should().Be(70);
    }

    /// <summary>
    /// The search refuses to answer with a plan it could not prove optimal,
    /// rather than quietly returning the best it happened to reach.
    /// </summary>
    [Fact]
    public void ExhaustedSearchBudget_ShouldFailInsteadOfGuessing()
    {
        var demands = Enumerable.Range(1, 4)
            .Select(number => FulfillmentTestData.Demand(number, 8))
            .ToArray();
        var offers = Enumerable.Range(1, 6)
            .SelectMany(vendor => Enumerable.Range(1, 4)
                .Select(product => FulfillmentTestData.Offer(
                    vendor, product, 10 + vendor, 8)))
            .ToArray();

        var result = FulfillmentTestData.Planner().CreateBestPlan(
            demands,
            offers,
            null,
            EvaluatedAt,
            FulfillmentPlannerOptions.Default with { MaxSearchNodes = 50 });

        result.Error.Should().Be(FulfillmentErrors.SearchBudgetExceeded);
    }

    /// <summary>
    /// The same input under the default budget still resolves, so the limit is a
    /// safety valve rather than a functional constraint on real carts.
    /// </summary>
    [Fact]
    public void RealisticCart_ShouldStayWellWithinTheDefaultBudget()
    {
        var demands = Enumerable.Range(1, 4)
            .Select(number => FulfillmentTestData.Demand(number, 8))
            .ToArray();
        var offers = Enumerable.Range(1, 6)
            .SelectMany(vendor => Enumerable.Range(1, 4)
                .Select(product => FulfillmentTestData.Offer(
                    vendor, product, 10 + vendor, 8)))
            .ToArray();

        var result = FulfillmentTestData.Planner().CreateBestPlan(
            demands, offers, null, EvaluatedAt);

        result.IsSuccess.Should().BeTrue();
        result.Value.VendorCount.Should().Be(1);
        result.Value.Vendors.Single().VendorId.Should()
            .Be(FulfillmentTestData.Vendor(1));
    }
}

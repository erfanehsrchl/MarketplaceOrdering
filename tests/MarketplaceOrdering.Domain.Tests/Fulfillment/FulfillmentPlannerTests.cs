using FluentAssertions;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Tests.Discounts;

namespace MarketplaceOrdering.Domain.Tests.Fulfillment;

public sealed class FulfillmentPlannerTests
{
    private static readonly DateTimeOffset EvaluatedAt =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateBestPlan_ShouldValidateDemandsAndOffers()
    {
        var planner = FulfillmentTestData.Planner();
        planner.CreateBestPlan(null, [], null, EvaluatedAt).Error.Code
            .Should().Be("fulfillment.demands_required");
        var demand = FulfillmentTestData.Demand(1, 1);
        planner.CreateBestPlan([demand, demand], [], null, EvaluatedAt).Error.Code
            .Should().Be("fulfillment.duplicate_product_demand");
        var offer = FulfillmentTestData.Offer(1, 1, 10, 1);
        planner.CreateBestPlan([demand], [offer, offer], null, EvaluatedAt)
            .Error.Code.Should().Be("fulfillment.duplicate_offer");
    }

    [Fact]
    public void IgnoredDuplicateAndIrrelevantOffer_ShouldNotConflict()
    {
        var demand = FulfillmentTestData.Demand(1, 1);
        var result = FulfillmentTestData.Planner().CreateBestPlan([demand],
        [
            FulfillmentTestData.Offer(1, 1, 10, 1),
            FulfillmentTestData.Offer(1, 1, 0, 1),
            FulfillmentTestData.Offer(1, 2, 10, 1)
        ], null, EvaluatedAt);
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InconsistentVendorTerms_ShouldFail(bool shippingDiffers)
    {
        var offers = new[]
        {
            FulfillmentTestData.Offer(1, 1, 10, 1, 5, 2, 10),
            FulfillmentTestData.Offer(1, 2, 10, 1,
                shippingDiffers ? 6 : 5, shippingDiffers ? 2 : 3, 20)
        };
        FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1), FulfillmentTestData.Demand(2, 1)],
            offers, null, EvaluatedAt).Error.Code
            .Should().Be("fulfillment.inconsistent_vendor_terms");
    }

    [Fact]
    public void CompletePlan_ShouldEnforceInventoryAndRejectPartialFulfillment()
    {
        var planner = FulfillmentTestData.Planner();
        planner.CreateBestPlan([FulfillmentTestData.Demand(1, 2)],
            [FulfillmentTestData.Offer(1, 1, 10, 1)], null, EvaluatedAt)
            .Error.Code.Should().Be("fulfillment.no_valid_plan");
        planner.CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1), FulfillmentTestData.Demand(2, 1)],
            [FulfillmentTestData.Offer(1, 1, 10, 1)], null, EvaluatedAt)
            .Error.Code.Should().Be("fulfillment.no_valid_plan");
    }

    [Fact]
    public void Plan_ShouldUseAtMostTwoVendorsPerProductAndThreeOverall()
    {
        var split = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 3)],
            [FulfillmentTestData.Offer(1, 1, 10, 2),
             FulfillmentTestData.Offer(2, 1, 10, 2)],
            null, EvaluatedAt).Value;
        split.ProductAllocations.Should().HaveCount(2);

        var fourVendorResult = FulfillmentTestData.Planner().CreateBestPlan(
            Enumerable.Range(1, 4).Select(i => FulfillmentTestData.Demand(i, 1)).ToArray(),
            Enumerable.Range(1, 4).Select(i => FulfillmentTestData.Offer(i, i, 10, 1)).ToArray(),
            null, EvaluatedAt);
        fourVendorResult.Error.Code.Should().Be("fulfillment.no_valid_plan");
    }

    [Fact]
    public void MinimumCanBeSatisfiedAcrossProductsAndExcludesShipping()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1), FulfillmentTestData.Demand(2, 1)],
            [FulfillmentTestData.Offer(1, 1, 60, 1, 100, 100, 10),
             FulfillmentTestData.Offer(1, 2, 40, 1, 100, 100, 20)],
            null, EvaluatedAt).Value;
        plan.ProductsAmount.Amount.Should().Be(100);
        plan.ShippingAmount.Amount.Should().Be(100);
        plan.Vendors.Single().EstimatedDeliveryHours.Should().Be(20);
    }

    [Fact]
    public void RequiredEqualTotalScenario_ShouldPreferOneVendor()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 3), FulfillmentTestData.Demand(2, 2)],
            [FulfillmentTestData.Offer(1, 1, 100, 3, 20, 0, 24),
             FulfillmentTestData.Offer(2, 2, 150, 2, 15, 0, 24),
             FulfillmentTestData.Offer(3, 1, 105, 3, 30, 0, 36),
             FulfillmentTestData.Offer(3, 2, 145, 2, 30, 0, 36)],
            null, EvaluatedAt).Value;
        plan.TotalPayable.Amount.Should().Be(635);
        plan.VendorCount.Should().Be(1);
        plan.Vendors.Single().VendorId.Should().Be(FulfillmentTestData.Vendor(3));
    }

    [Fact]
    public void Discount_ShouldRankFinalPayableAndNeverDiscountShipping()
    {
        var policy = DiscountPolicy.Create(
            DiscountTestData.Code("FIXED"),
            FixedDiscountValue.Create(FulfillmentTestData.Money(50)).Value,
            true, eligibleVendorIds: [FulfillmentTestData.Vendor(2)]).Value;
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1)],
            [FulfillmentTestData.Offer(1, 1, 100, 1, 5),
             FulfillmentTestData.Offer(2, 1, 110, 1, 10)],
            policy, EvaluatedAt).Value;

        plan.Vendors.Single().VendorId.Should().Be(FulfillmentTestData.Vendor(2));
        plan.ProductsAmount.Amount.Should().Be(110);
        plan.DiscountAmount.Amount.Should().Be(50);
        plan.ShippingAmount.Amount.Should().Be(10);
        plan.TotalPayable.Amount.Should().Be(70);
        plan.DiscountCalculation.Should().NotBeNull();
        plan.Vendors.Sum(v => v.DiscountAmount.Amount).Should().Be(50);
    }

    [Fact]
    public void AllCandidatesFailingDiscount_ShouldReturnSpecificError()
    {
        var policy = DiscountPolicy.Create(
            DiscountTestData.Code(), DiscountTestData.Percentage(), true,
            eligibleVendorIds: [FulfillmentTestData.Vendor(99)]).Value;
        var result = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(1, 1)],
            [FulfillmentTestData.Offer(1, 1, 100, 1)],
            policy, EvaluatedAt);
        result.Error.Code.Should().Be("discount.not_applicable");
    }
}

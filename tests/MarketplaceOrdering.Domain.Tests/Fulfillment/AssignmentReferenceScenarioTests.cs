using FluentAssertions;
using MarketplaceOrdering.Domain.Fulfillment;

namespace MarketplaceOrdering.Domain.Tests.Fulfillment;

/// <summary>
/// The worked example from the assignment brief, with its exact numbers.
/// </summary>
/// <remarks>
/// <para>
/// The brief presents two combinations that both total 635 and concludes that
/// the single-Vendor one wins on the "fewer Vendors" tie-break. Both statements
/// are true, but they are not the answer: with these Offers a cheaper valid
/// combination exists that the brief does not mention.
/// </para>
/// <code>
/// Vendor 1  Product A x3  3 x 100 = 300   minimum 200 satisfied   shipping 20
/// Vendor 3  Product B x2  2 x 140 = 280   minimum 100 satisfied   shipping 25
/// Products 580 + Shipping 45          =   625
/// </code>
/// <para>
/// 625 &lt; 635, every stock limit holds, each Product comes from one Vendor, and
/// only two Vendors are used. Ranking is defined as cheapest first, so returning
/// 635 here would mean the planner had failed to find the optimum. These tests
/// therefore assert 625, and separately assert the tie-break behaviour the brief
/// intended to demonstrate on Offers where the tie is real.
/// </para>
/// </remarks>
public sealed class AssignmentReferenceScenarioTests
{
    private static readonly DateTimeOffset EvaluatedAt =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private const int ProductA = 1;
    private const int ProductB = 2;

    /// <summary>Offers exactly as printed in the assignment brief.</summary>
    private static ProductOffer[] AssignmentOffers() =>
    [
        FulfillmentTestData.Offer(1, ProductA, 100, 3, 20, 200, 48),
        FulfillmentTestData.Offer(1, ProductB, 150, 1, 20, 200, 48),
        FulfillmentTestData.Offer(2, ProductA, 95, 2, 15, 250, 24),
        FulfillmentTestData.Offer(2, ProductB, 160, 2, 15, 250, 24),
        FulfillmentTestData.Offer(3, ProductA, 110, 3, 25, 100, 12),
        FulfillmentTestData.Offer(3, ProductB, 140, 2, 25, 100, 12)
    ];

    [Fact]
    public void AssignmentOffers_ShouldFindThePlanCheaperThanTheBriefsExample()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [
                FulfillmentTestData.Demand(ProductA, 3),
                FulfillmentTestData.Demand(ProductB, 2)
            ],
            AssignmentOffers(),
            null,
            EvaluatedAt).Value;

        plan.TotalPayable.Amount.Should().Be(625);
        plan.ProductsAmount.Amount.Should().Be(580);
        plan.ShippingAmount.Amount.Should().Be(45);
        plan.VendorCount.Should().Be(2);
        plan.Vendors.Select(vendor => vendor.VendorId).Should().BeEquivalentTo(
            new[] { FulfillmentTestData.Vendor(1), FulfillmentTestData.Vendor(3) });

        var vendorOne = plan.Vendors.Single(
            vendor => vendor.VendorId == FulfillmentTestData.Vendor(1));
        vendorOne.ProductAllocations.Should().ContainSingle()
            .Which.Quantity.Value.Should().Be(3);
        vendorOne.ProductsAmount.Amount.Should().Be(300);

        var vendorThree = plan.Vendors.Single(
            vendor => vendor.VendorId == FulfillmentTestData.Vendor(3));
        vendorThree.ProductAllocations.Should().ContainSingle()
            .Which.Quantity.Value.Should().Be(2);
        vendorThree.ProductsAmount.Amount.Should().Be(280);
    }

    /// <summary>
    /// Both combinations the brief describes are valid and both really do total
    /// 635, which is the premise of its tie-break argument.
    /// </summary>
    [Fact]
    public void BothCombinationsFromTheBrief_ShouldBeValidAndTotal635()
    {
        var planner = FulfillmentTestData.Planner();

        // Force the brief's first combination by leaving Vendor 3 out.
        var twoVendorPlan = planner.CreateBestPlan(
            [
                FulfillmentTestData.Demand(ProductA, 3),
                FulfillmentTestData.Demand(ProductB, 2)
            ],
            AssignmentOffers().Where(offer =>
                offer.VendorId != FulfillmentTestData.Vendor(3)).ToArray(),
            null,
            EvaluatedAt).Value;

        twoVendorPlan.TotalPayable.Amount.Should().Be(635);
        twoVendorPlan.VendorCount.Should().Be(2);

        // Force the brief's second combination by leaving Vendors 1 and 2 out.
        var singleVendorPlan = planner.CreateBestPlan(
            [
                FulfillmentTestData.Demand(ProductA, 3),
                FulfillmentTestData.Demand(ProductB, 2)
            ],
            AssignmentOffers().Where(offer =>
                offer.VendorId == FulfillmentTestData.Vendor(3)).ToArray(),
            null,
            EvaluatedAt).Value;

        singleVendorPlan.TotalPayable.Amount.Should().Be(635);
        singleVendorPlan.VendorCount.Should().Be(1);
        singleVendorPlan.ProductsAmount.Amount.Should().Be(610);
        singleVendorPlan.ShippingAmount.Amount.Should().Be(25);
    }

    /// <summary>
    /// The behaviour the brief's example was meant to prove: on a genuine tie in
    /// money, the plan using fewer Vendors wins.
    /// </summary>
    [Fact]
    public void GenuineMoneyTie_ShouldPreferFewerVendors()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [
                FulfillmentTestData.Demand(ProductA, 3),
                FulfillmentTestData.Demand(ProductB, 2)
            ],
            [
                FulfillmentTestData.Offer(1, ProductA, 100, 3, 20, 0, 24),
                FulfillmentTestData.Offer(2, ProductB, 150, 2, 15, 0, 24),
                FulfillmentTestData.Offer(3, ProductA, 105, 3, 30, 0, 36),
                FulfillmentTestData.Offer(3, ProductB, 145, 2, 30, 0, 36)
            ],
            null,
            EvaluatedAt).Value;

        plan.TotalPayable.Amount.Should().Be(635);
        plan.VendorCount.Should().Be(1);
        plan.Vendors.Single().VendorId.Should()
            .Be(FulfillmentTestData.Vendor(3));
    }

    /// <summary>
    /// Equal money and equal Vendor count: the faster plan wins on the third
    /// criterion, the slowest Vendor in the plan.
    /// </summary>
    [Fact]
    public void MoneyAndVendorCountTie_ShouldPreferFasterDelivery()
    {
        var plan = FulfillmentTestData.Planner().CreateBestPlan(
            [FulfillmentTestData.Demand(ProductA, 1)],
            [
                FulfillmentTestData.Offer(1, ProductA, 100, 1, 10, 0, 48),
                FulfillmentTestData.Offer(2, ProductA, 100, 1, 10, 0, 12)
            ],
            null,
            EvaluatedAt).Value;

        plan.TotalPayable.Amount.Should().Be(110);
        plan.MaximumDeliveryHours.Should().Be(12);
        plan.Vendors.Single().VendorId.Should()
            .Be(FulfillmentTestData.Vendor(2));
    }
}

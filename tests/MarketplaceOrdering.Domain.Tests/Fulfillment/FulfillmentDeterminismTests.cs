using FluentAssertions;
using MarketplaceOrdering.Domain.Fulfillment;

namespace MarketplaceOrdering.Domain.Tests.Fulfillment;

public sealed class FulfillmentDeterminismTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    [Fact]
    public void InputOrderAndRepeatedExecution_ShouldNotChangePlan()
    {
        var demands = new[]
        {
            FulfillmentTestData.Demand(2, 2),
            FulfillmentTestData.Demand(1, 3)
        };
        var offers = new[]
        {
            FulfillmentTestData.Offer(2, 2, 20, 2, 5),
            FulfillmentTestData.Offer(1, 1, 10, 3, 4),
            FulfillmentTestData.Offer(2, 1, 11, 3, 5),
            FulfillmentTestData.Offer(1, 2, 21, 2, 4)
        };
        var planner = FulfillmentTestData.Planner();
        var expected = Signature(planner.CreateBestPlan(
            demands, offers, null, At).Value);

        for (var repetition = 0; repetition < 100; repetition++)
        {
            Signature(planner.CreateBestPlan(
                demands.Reverse().ToArray(),
                offers.Reverse().ToArray(), null, At).Value)
                .Should().Be(expected);
        }
    }

    [Fact]
    public void TwoHundredGeneratedPlans_ShouldPreserveAllInvariants()
    {
        var random = new Random(24680);
        for (var scenario = 0; scenario < 200; scenario++)
        {
            var quantity = random.Next(1, 6);
            var price = random.Next(1, 1_000);
            var shipping = random.Next(0, 100);
            var demand = FulfillmentTestData.Demand(1, quantity);
            var offer = FulfillmentTestData.Offer(
                1, 1, price, quantity, shipping, price * quantity);

            var plan = FulfillmentTestData.Planner().CreateBestPlan(
                [demand], [offer], null, At).Value;

            plan.VendorCount.Should().BeLessThanOrEqualTo(3);
            plan.ProductAllocations.Sum(a => a.Quantity.Value).Should().Be(quantity);
            plan.ProductAllocations.Should().OnlyContain(
                allocation => allocation.Quantity.Value <= offer.AvailableQuantity);
            plan.Vendors.Single().ProductsAmount.Amount
                .Should().BeGreaterThanOrEqualTo(offer.MinimumOrderAmount.Amount);
            plan.ProductsAmount.Amount.Should().Be(
                plan.ProductAllocations.Sum(a => a.LineTotal.Amount));
            plan.DiscountAmount.Amount.Should().Be(
                plan.Vendors.Sum(v => v.DiscountAmount.Amount));
            plan.ShippingAmount.Amount.Should().Be(
                plan.Vendors.Sum(v => v.ShippingCost.Amount));
            plan.TotalPayable.Amount.Should().Be(
                plan.ProductsAmount.Amount - plan.DiscountAmount.Amount
                + plan.ShippingAmount.Amount);
            plan.Vendors.Select(v => v.VendorId.Value)
                .Should().BeInAscendingOrder();
            var mutation = () =>
                ((ICollection<VendorFulfillment>)plan.Vendors).Clear();
            mutation.Should().Throw<NotSupportedException>();
        }
    }

    private static string Signature(FulfillmentPlan plan) =>
        string.Join("|", plan.ProductAllocations.Select(allocation =>
            $"{allocation.VendorId}:{allocation.ProductId}:" +
            $"{allocation.Quantity.Value}:{allocation.UnitPrice.Amount}"))
        + $"/{plan.TotalPayable.Amount}";
}

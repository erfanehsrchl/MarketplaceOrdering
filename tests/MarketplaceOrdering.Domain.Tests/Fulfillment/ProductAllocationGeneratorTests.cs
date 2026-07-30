using FluentAssertions;
using MarketplaceOrdering.Domain.Fulfillment;

namespace MarketplaceOrdering.Domain.Tests.Fulfillment;

public sealed class ProductAllocationGeneratorTests
{
    private readonly ProductAllocationGenerator _generator = new();

    [Fact]
    public void Generate_ShouldCreateSingleAndEveryValidTwoVendorSplit()
    {
        var demand = FulfillmentTestData.Demand(1, 3);
        var offers = new[]
        {
            FulfillmentTestData.Offer(2, 1, 10, 3),
            FulfillmentTestData.Offer(1, 1, 10, 3)
        };

        var options = _generator.Generate(demand, offers).Value;

        options.Should().HaveCount(4);
        options.Count(option => option.Allocations.Count == 1).Should().Be(2);
        options.Where(option => option.Allocations.Count == 2)
            .Select(option => option.Allocations.First().Quantity.Value)
            .Should().ContainInOrder(1, 2);
        options.Should().OnlyContain(option =>
            option.Allocations.Sum(allocation => allocation.Quantity.Value) == 3);
    }

    [Fact]
    public void Generate_ShouldOmitInsufficientAndIgnoredOffers()
    {
        var demand = FulfillmentTestData.Demand(1, 2);
        var options = _generator.Generate(demand,
        [
            FulfillmentTestData.Offer(1, 1, 10, 1),
            FulfillmentTestData.Offer(2, 1, 0, 5),
            FulfillmentTestData.Offer(3, 1, 10, 0)
        ]).Value;

        options.Should().BeEmpty();
    }

    [Fact]
    public void Generate_QuantityOne_ShouldHaveNoSplitAndIgnoreInputOrder()
    {
        var demand = FulfillmentTestData.Demand(1, 1);
        var first = FulfillmentTestData.Offer(1, 1, 10, 1);
        var second = FulfillmentTestData.Offer(2, 1, 10, 1);

        var forward = _generator.Generate(demand, [first, second]).Value;
        var reverse = _generator.Generate(demand, [second, first]).Value;

        forward.Should().AllSatisfy(option =>
            option.Allocations.Should().ContainSingle());
        reverse.SelectMany(option => option.Allocations)
            .Select(allocation => allocation.VendorId)
            .Should().Equal(forward.SelectMany(option => option.Allocations)
                .Select(allocation => allocation.VendorId));
    }
}

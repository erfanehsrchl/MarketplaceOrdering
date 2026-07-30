using FluentAssertions;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Offers;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Offers;

public sealed class InMemoryProductOfferProviderTests
{
    [Fact]
    public async Task ReturnsOnlyDemandedOffersInDeterministicOrder()
    {
        var provider = new InMemoryProductOfferProvider();
        var input = new List<ProductOffer>
        {
            InfrastructureTestData.Offer(1, 2),
            InfrastructureTestData.Offer(2, 1),
            InfrastructureTestData.Offer(1, 1)
        };
        provider.ReplaceOffers(input);
        input.Clear();
        var demand = new ProductDemand(
            new ProductReference(
                InfrastructureTestData.Product(1),
                ProductName.Create("One").Value),
            Quantity.Create(1).Value);

        var result = await provider.GetOffersAsync([demand], default);

        result.Value.Should().HaveCount(2);
        result.Value.Select(offer => offer.VendorId)
            .Should().ContainInOrder(
                InfrastructureTestData.Vendor(1),
                InfrastructureTestData.Vendor(2));
    }

    [Fact]
    public async Task EmptyConfigurationReturnsCopiedEmptyResult()
    {
        var provider = new InMemoryProductOfferProvider();
        var result = await provider.GetOffersAsync([], default);
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var provider = new InMemoryProductOfferProvider();
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetOffersAsync([], token));
    }
}

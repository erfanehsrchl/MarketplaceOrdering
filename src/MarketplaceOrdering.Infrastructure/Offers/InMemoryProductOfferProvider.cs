using MarketplaceOrdering.Application.Common.Abstractions.Offers;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Infrastructure.Offers;

public sealed class InMemoryProductOfferProvider : IProductOfferProvider
{
    private readonly object _syncRoot = new();
    private ProductOffer[] _offers = [];

    public void ReplaceOffers(IEnumerable<ProductOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        var copy = offers.ToArray();
        lock (_syncRoot)
            _offers = copy;
    }

    public void Clear()
    {
        lock (_syncRoot)
            _offers = [];
    }

    public Task<Result<IReadOnlyCollection<ProductOffer>>> GetOffersAsync(
        IReadOnlyCollection<ProductDemand> demands,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(demands);
        var productIds = demands.Select(demand => demand.Product.ProductId)
            .ToHashSet();
        ProductOffer[] result;
        lock (_syncRoot)
            result = _offers
                .Where(offer => productIds.Contains(offer.ProductId))
                .OrderBy(offer => offer.ProductId.Value)
                .ThenBy(offer => offer.VendorId.Value)
                .ToArray();
        return Task.FromResult(
            Result<IReadOnlyCollection<ProductOffer>>.Success(result));
    }
}

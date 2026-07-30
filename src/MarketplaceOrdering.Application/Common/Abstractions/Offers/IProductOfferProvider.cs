using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Common.Abstractions.Offers;

public interface IProductOfferProvider
{
    Task<Result<IReadOnlyCollection<ProductOffer>>> GetOffersAsync(
        IReadOnlyCollection<ProductDemand> demands,
        CancellationToken cancellationToken);
}

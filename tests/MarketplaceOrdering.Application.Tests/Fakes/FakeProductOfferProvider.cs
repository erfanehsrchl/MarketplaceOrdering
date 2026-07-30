using MarketplaceOrdering.Application.Common.Abstractions.Offers;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal sealed class FakeProductOfferProvider : IProductOfferProvider
{
    internal IReadOnlyCollection<ProductOffer> Offers { get; set; } = [];
    internal Error? Failure { get; set; }
    internal int CallCount { get; private set; }
    internal IReadOnlyCollection<ProductDemand>? CapturedDemands { get; private set; }
    internal CancellationToken CapturedCancellationToken { get; private set; }
    internal IList<string>? Journal { get; set; }

    public Task<Result<IReadOnlyCollection<ProductOffer>>> GetOffersAsync(
        IReadOnlyCollection<ProductDemand> demands,
        CancellationToken cancellationToken)
    {
        CallCount++;
        Journal?.Add("Offers.Get");
        CapturedDemands = demands.ToArray();
        CapturedCancellationToken = cancellationToken;
        return Task.FromResult(Failure is null
            ? Result<IReadOnlyCollection<ProductOffer>>.Success(Offers.ToArray())
            : Result<IReadOnlyCollection<ProductOffer>>.Failure(Failure));
    }
}

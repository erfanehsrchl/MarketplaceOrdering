using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal sealed class FakeDiscountPolicyProvider : IDiscountPolicyProvider
{
    internal DiscountPolicy? Policy { get; set; }
    internal Error? Failure { get; set; }
    internal int CallCount { get; private set; }
    internal DiscountCode? CapturedCode { get; private set; }
    internal CancellationToken CapturedCancellationToken { get; private set; }
    internal IList<string>? Journal { get; set; }

    public Task<Result<DiscountPolicy>> GetByCodeAsync(
        DiscountCode code,
        CancellationToken cancellationToken)
    {
        CallCount++;
        Journal?.Add("Discount.Get");
        CapturedCode = code;
        CapturedCancellationToken = cancellationToken;
        if (Failure is not null)
            return Task.FromResult(Result<DiscountPolicy>.Failure(Failure));
        return Task.FromResult(Policy is null
            ? Result<DiscountPolicy>.Failure(
                Error.NotFound("discount.not_found", "Discount not found."))
            : Result<DiscountPolicy>.Success(Policy));
    }
}

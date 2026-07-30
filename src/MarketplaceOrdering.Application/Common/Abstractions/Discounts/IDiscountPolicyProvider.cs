using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Discounts;

public interface IDiscountPolicyProvider
{
    Task<Result<DiscountPolicy>> GetByCodeAsync(
        DiscountCode code,
        CancellationToken cancellationToken);
}

using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Errors;

namespace MarketplaceOrdering.Infrastructure.Discounts;

public sealed class InMemoryDiscountPolicyProvider : IDiscountPolicyProvider
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DiscountPolicy> _policies =
        new(StringComparer.Ordinal);

    public void UpsertPolicy(DiscountPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        lock (_syncRoot)
            _policies[policy.Code.Value] = policy;
    }

    public void ReplacePolicies(IEnumerable<DiscountPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var copy = policies.ToArray();
        lock (_syncRoot)
        {
            _policies.Clear();
            foreach (var policy in copy)
                _policies[policy.Code.Value] = policy;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
            _policies.Clear();
    }

    public Task<Result<DiscountPolicy>> GetByCodeAsync(
        DiscountCode code,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(code);
        lock (_syncRoot)
            return Task.FromResult(_policies.TryGetValue(code.Value, out var policy)
                ? Result<DiscountPolicy>.Success(policy)
                : Result<DiscountPolicy>.Failure(
                    InfrastructureErrors.DiscountPolicyNotFound));
    }
}

using FluentAssertions;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Discounts;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Discounts;

public sealed class InMemoryDiscountPolicyProviderTests
{
    [Fact]
    public async Task UpsertReplacesPolicyByNormalizedCode()
    {
        var provider = new InMemoryDiscountPolicyProvider();
        provider.UpsertPolicy(InfrastructureTestData.Policy("save", 10));
        var latest = InfrastructureTestData.Policy("SAVE", 20);
        provider.UpsertPolicy(latest);

        var result = await provider.GetByCodeAsync(
            DiscountCode.Create(" save ").Value, default);

        result.Value.Should().BeSameAs(latest);
    }

    [Fact]
    public async Task ReplaceCopiesAndClearRemovesPolicies()
    {
        var provider = new InMemoryDiscountPolicyProvider();
        var policies = new List<Domain.Discounts.DiscountPolicy>
        {
            InfrastructureTestData.Policy("A", 10)
        };
        provider.ReplacePolicies(policies);
        policies.Clear();
        (await provider.GetByCodeAsync(
            DiscountCode.Create("A").Value, default)).IsSuccess.Should().BeTrue();

        provider.Clear();
        var missing = await provider.GetByCodeAsync(
            DiscountCode.Create("A").Value, default);
        missing.Error.Code.Should().Be("discount.policy_not_found");
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var provider = new InMemoryDiscountPolicyProvider();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetByCodeAsync(
                DiscountCode.Create("A").Value,
                new CancellationToken(true)));
    }
}

using FluentAssertions;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Discounts;

public sealed class DiscountPolicyCreationTests
{
    [Fact]
    public void Create_ShouldAcceptPercentageAndFixedPolicies()
    {
        DiscountPolicy.Create(
            DiscountTestData.Code(),
            DiscountTestData.Percentage(),
            true).IsSuccess.Should().BeTrue();
        DiscountPolicy.Create(
            DiscountTestData.Code(),
            DiscountTestData.Fixed(),
            true).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldSupportOpenDateRange()
    {
        var policy = DiscountPolicy.Create(
            DiscountTestData.Code(),
            DiscountTestData.Percentage(),
            true).Value;

        policy.StartsAt.Should().BeNull();
        policy.EndsAt.Should().BeNull();
    }

    [Fact]
    public void Create_WithEqualStartAndEnd_ShouldSucceed()
    {
        DiscountPolicy.Create(
            DiscountTestData.Code(),
            DiscountTestData.Percentage(),
            true,
            DiscountTestData.EvaluatedAt,
            DiscountTestData.EvaluatedAt).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WithStartAfterEnd_ShouldFail()
    {
        DiscountPolicy.Create(
            DiscountTestData.Code(),
            DiscountTestData.Percentage(),
            true,
            DiscountTestData.EvaluatedAt.AddSeconds(1),
            DiscountTestData.EvaluatedAt).Error.Code
            .Should().Be("discount.invalid_date_range");
    }

    [Fact]
    public void Create_WithPositiveMaximum_ShouldSucceed()
    {
        DiscountPolicy.Create(
            DiscountTestData.Code(),
            DiscountTestData.Percentage(),
            true,
            maximumDiscountAmount: DiscountTestData.Money(1))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WithZeroMaximum_ShouldFail()
    {
        DiscountPolicy.Create(
            DiscountTestData.Code(),
            DiscountTestData.Percentage(),
            true,
            maximumDiscountAmount: DiscountTestData.Money(0))
            .Error.Code.Should().Be("discount.maximum_amount_not_positive");
    }

    [Fact]
    public void Create_WithZeroMinimum_ShouldSucceed()
    {
        DiscountPolicy.Create(
            DiscountTestData.Code(),
            DiscountTestData.Percentage(),
            true,
            minimumProductsAmount: DiscountTestData.Money(0))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WithEmptyEligibleList_ShouldBeUnrestricted()
    {
        DiscountTestData.Policy(eligibleVendorIds: Array.Empty<VendorId>())
            .EligibleVendorIds.Should().BeEmpty();
    }

    [Fact]
    public void Create_ShouldNormalizeDuplicatesAndOrderVendorIds()
    {
        var first = DiscountTestData.Vendor(1);
        var second = DiscountTestData.Vendor(2);

        var policy = DiscountTestData.Policy(
            eligibleVendorIds: [second, first, second]);

        policy.EligibleVendorIds.Should().ContainInOrder(first, second);
    }

    [Fact]
    public void EligibleVendorIds_ShouldBeReadOnly()
    {
        var policy = DiscountTestData.Policy(
            eligibleVendorIds: [DiscountTestData.Vendor(1)]);

        var mutation = () =>
            ((ICollection<VendorId>)policy.EligibleVendorIds).Clear();

        mutation.Should().Throw<NotSupportedException>();
    }
}

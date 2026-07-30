using FluentAssertions;
using MarketplaceOrdering.Domain.Discounts;

namespace MarketplaceOrdering.Domain.Tests.Discounts;

public sealed class DiscountPolicyEvaluationTests
{
    private readonly ProportionalDiscountAllocator _allocator = new();

    [Fact]
    public void Evaluate_InactivePolicy_ShouldFailFirst()
    {
        var policy = DiscountTestData.Policy(
            isActive: false,
            startsAt: DiscountTestData.EvaluatedAt.AddDays(1),
            minimum: DiscountTestData.Money(2_000),
            eligibleVendorIds: [DiscountTestData.Vendor(99)]);

        policy.Evaluate(DiscountTestData.Context(), _allocator).Error.Code
            .Should().Be("discount.inactive");
    }

    [Fact]
    public void Evaluate_BeforeStart_ShouldFail()
    {
        var policy = DiscountTestData.Policy(
            startsAt: DiscountTestData.EvaluatedAt.AddSeconds(1));

        policy.Evaluate(DiscountTestData.Context(), _allocator).Error.Code
            .Should().Be("discount.not_started");
    }

    [Fact]
    public void Evaluate_ExactlyAtStart_ShouldSucceed()
    {
        var policy = DiscountTestData.Policy(
            startsAt: DiscountTestData.EvaluatedAt);

        policy.Evaluate(DiscountTestData.Context(), _allocator)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ExactlyAtEnd_ShouldSucceed()
    {
        var policy = DiscountTestData.Policy(
            endsAt: DiscountTestData.EvaluatedAt);

        policy.Evaluate(DiscountTestData.Context(), _allocator)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AfterEnd_ShouldFail()
    {
        var end = DiscountTestData.EvaluatedAt.AddSeconds(-1);
        var policy = DiscountTestData.Policy(endsAt: end);

        policy.Evaluate(DiscountTestData.Context(), _allocator).Error.Code
            .Should().Be("discount.expired");
    }

    [Fact]
    public void Evaluate_WithNoStartOrEnd_ShouldSucceed()
    {
        DiscountTestData.Policy()
            .Evaluate(DiscountTestData.Context(), _allocator)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_TotalBelowMinimum_ShouldFailWithMetadata()
    {
        var policy = DiscountTestData.Policy(
            minimum: DiscountTestData.Money(1_001));

        var result = policy.Evaluate(DiscountTestData.Context(), _allocator);

        result.Error.Code.Should().Be("discount.minimum_amount_not_met");
        result.Error.Metadata.Should().Contain("requiredAmount", "1001");
        result.Error.Metadata.Should().Contain("actualAmount", "1000");
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(999)]
    public void Evaluate_TotalAtOrAboveMinimum_ShouldSucceed(long minimum)
    {
        var policy = DiscountTestData.Policy(
            minimum: DiscountTestData.Money(minimum));

        policy.Evaluate(DiscountTestData.Context(), _allocator)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MinimumShouldUseFullTotalButDiscountOnlyEligibleAmount()
    {
        var eligible = DiscountTestData.VendorAmount(1, 300);
        var ineligible = DiscountTestData.VendorAmount(2, 700);
        var context = DiscountTestData.Context(
            vendorAmounts: [eligible, ineligible]);
        var policy = DiscountTestData.Policy(
            minimum: DiscountTestData.Money(800),
            eligibleVendorIds: [eligible.VendorId]);

        var calculation = policy.Evaluate(context, _allocator).Value;

        calculation.TotalProductsAmount.Amount.Should().Be(1_000);
        calculation.EligibleProductsAmount.Amount.Should().Be(300);
        calculation.TotalDiscountAmount.Amount.Should().Be(30);
        calculation.VendorAllocations.Should().ContainSingle()
            .Which.VendorId.Should().Be(eligible.VendorId);
    }

    [Fact]
    public void Evaluate_EmptyEligibilityList_ShouldApplyToAllVendors()
    {
        var context = DiscountTestData.Context(
            vendorAmounts:
            [
                DiscountTestData.VendorAmount(1, 600),
                DiscountTestData.VendorAmount(2, 400)
            ]);

        var calculation = DiscountTestData.Policy()
            .Evaluate(context, _allocator).Value;

        calculation.EligibleProductsAmount.Should().Be(
            context.TotalProductsAmount);
        calculation.VendorAllocations.Should().HaveCount(2);
    }

    [Fact]
    public void Evaluate_RestrictedPolicy_ShouldExcludeIneligibleVendors()
    {
        var eligible = DiscountTestData.VendorAmount(1, 600);
        var ineligible = DiscountTestData.VendorAmount(2, 400);
        var context = DiscountTestData.Context(
            vendorAmounts: [eligible, ineligible]);
        var policy = DiscountTestData.Policy(
            eligibleVendorIds: [eligible.VendorId]);

        var calculation = policy.Evaluate(context, _allocator).Value;

        calculation.TotalProductsAmount.Amount.Should().Be(1_000);
        calculation.EligibleProductsAmount.Amount.Should().Be(600);
        calculation.TotalDiscountAmount.Amount.Should().Be(60);
        calculation.VendorAllocations.Should().ContainSingle()
            .Which.VendorId.Should().Be(eligible.VendorId);
        calculation.VendorAllocations.Should().NotContain(
            allocation => allocation.VendorId == ineligible.VendorId);
    }

    [Fact]
    public void Evaluate_WithNoMatchingVendor_ShouldFail()
    {
        var policy = DiscountTestData.Policy(
            eligibleVendorIds: [DiscountTestData.Vendor(99)]);

        policy.Evaluate(DiscountTestData.Context(), _allocator).Error.Code
            .Should().Be("discount.not_applicable");
    }
}

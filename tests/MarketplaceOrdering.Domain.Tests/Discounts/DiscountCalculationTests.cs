using FluentAssertions;
using MarketplaceOrdering.Domain.Discounts;

namespace MarketplaceOrdering.Domain.Tests.Discounts;

public sealed class DiscountCalculationTests
{
    private readonly ProportionalDiscountAllocator _allocator = new();

    [Fact]
    public void Percentage_ShouldCalculateSimpleTotal()
    {
        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Percentage(12.5m))
            .Evaluate(DiscountTestData.Context(), _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(125);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(15, 2)]
    public void Percentage_ShouldUseMidpointRoundingToEven(
        long productsAmount,
        long expectedDiscount)
    {
        var context = DiscountTestData.Context(
            vendorAmounts: [DiscountTestData.VendorAmount(1, productsAmount)]);

        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Percentage(10m))
            .Evaluate(context, _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(expectedDiscount);
    }

    [Fact]
    public void Percentage_ShouldRoundTotalOnceBeforeAllocation()
    {
        var context = DiscountTestData.Context(
            vendorAmounts:
            [
                DiscountTestData.VendorAmount(1, 1),
                DiscountTestData.VendorAmount(2, 1)
            ]);

        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Percentage(30m))
            .Evaluate(context, _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(1);
        calculation.VendorAllocations.Should().ContainSingle();
        calculation.VendorAllocations.Single().VendorId.Should()
            .Be(DiscountTestData.Vendor(1));
    }

    [Fact]
    public void Percentage_TinyCalculatedDiscount_ShouldSucceedWithEmptyAllocations()
    {
        var context = DiscountTestData.Context(
            vendorAmounts: [DiscountTestData.VendorAmount(1, 1)]);

        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Percentage(0.01m))
            .Evaluate(context, _allocator).Value;

        calculation.TotalDiscountAmount.Should().Be(DiscountTestData.Money(0));
        calculation.VendorAllocations.Should().BeEmpty();
    }

    [Fact]
    public void Percentage_MaximumAmountShouldCapRoundedTotal()
    {
        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Percentage(30m),
                maximum: DiscountTestData.Money(100))
            .Evaluate(DiscountTestData.Context(), _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(100);
    }

    [Fact]
    public void Fixed_BelowEligibleAmount_ShouldBePreservedWithoutRounding()
    {
        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Fixed(123))
            .Evaluate(DiscountTestData.Context(), _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(123);
    }

    [Fact]
    public void Fixed_EqualToEligibleAmount_ShouldBePreserved()
    {
        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Fixed(1_000))
            .Evaluate(DiscountTestData.Context(), _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(1_000);
        calculation.VendorAllocations.Single().DiscountAmount.Amount
            .Should().Be(1_000);
    }

    [Fact]
    public void Fixed_AboveEligibleAmount_ShouldBeCappedToPreventNegativePayable()
    {
        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Fixed(2_000))
            .Evaluate(DiscountTestData.Context(), _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(1_000);
        (calculation.EligibleProductsAmount.Amount
            - calculation.TotalDiscountAmount.Amount).Should().Be(0);
    }

    [Fact]
    public void Fixed_MaximumAmountShouldCapDiscount()
    {
        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Fixed(500),
                maximum: DiscountTestData.Money(200))
            .Evaluate(DiscountTestData.Context(), _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(200);
    }

    [Fact]
    public void Fixed_ShouldAllocateOnlyToEligibleVendors()
    {
        var eligible = DiscountTestData.VendorAmount(1, 300);
        var context = DiscountTestData.Context(
            vendorAmounts:
            [
                eligible,
                DiscountTestData.VendorAmount(2, 700)
            ]);

        var calculation = DiscountTestData.Policy(
                value: DiscountTestData.Fixed(200),
                eligibleVendorIds: [eligible.VendorId])
            .Evaluate(context, _allocator).Value;

        calculation.TotalDiscountAmount.Amount.Should().Be(200);
        calculation.VendorAllocations.Should().ContainSingle()
            .Which.VendorId.Should().Be(eligible.VendorId);
    }

    [Fact]
    public void Calculation_ShouldStoreAllDataAndProtectAllocations()
    {
        var value = DiscountTestData.Percentage(10m);
        var context = DiscountTestData.Context();
        var policy = DiscountTestData.Policy(value: value);

        var calculation = policy.Evaluate(context, _allocator).Value;

        calculation.Code.Should().Be(policy.Code);
        calculation.AppliedValue.Should().Be(value);
        calculation.TotalProductsAmount.Should().Be(context.TotalProductsAmount);
        calculation.EligibleProductsAmount.Amount.Should().Be(1_000);
        calculation.TotalDiscountAmount.Amount.Should().Be(100);
        calculation.VendorAllocations.Sum(allocation =>
            allocation.DiscountAmount.Amount).Should().Be(100);
        calculation.EvaluatedAt.Should().Be(context.EvaluatedAt);

        var mutation = () =>
            ((ICollection<VendorDiscountAllocation>)calculation.VendorAllocations)
            .Clear();
        mutation.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Calculation_ShouldPreserveFixedAppliedValue()
    {
        var value = DiscountTestData.Fixed(250);

        var calculation = DiscountTestData.Policy(value: value)
            .Evaluate(DiscountTestData.Context(), _allocator).Value;

        calculation.AppliedValue.Should().Be(value);
    }

    [Fact]
    public void Calculation_ShouldRejectAllocationSumMismatch()
    {
        var result = DiscountCalculation.Create(
            DiscountTestData.Code(),
            DiscountTestData.Percentage(),
            DiscountTestData.Money(1_000),
            DiscountTestData.Money(1_000),
            DiscountTestData.Money(100),
            [
                new VendorDiscountAllocation(
                    DiscountTestData.Vendor(1),
                    DiscountTestData.Money(99))
            ],
            DiscountTestData.EvaluatedAt);

        result.Error.Code.Should().Be("discount.allocation_failed");
    }
}

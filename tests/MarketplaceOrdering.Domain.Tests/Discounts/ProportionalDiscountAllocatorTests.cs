using FluentAssertions;
using MarketplaceOrdering.Domain.Discounts;

namespace MarketplaceOrdering.Domain.Tests.Discounts;

public sealed class ProportionalDiscountAllocatorTests
{
    private readonly ProportionalDiscountAllocator _allocator = new();

    [Fact]
    public void Allocate_ShouldUseExactProportions()
    {
        var first = DiscountTestData.VendorAmount(1, 600);
        var second = DiscountTestData.VendorAmount(2, 400);

        var allocations = _allocator.Allocate(
            DiscountTestData.Money(100),
            [first, second]).Value;

        allocations.Should().HaveCount(2);
        allocations.Single(allocation => allocation.VendorId == first.VendorId)
            .DiscountAmount.Amount.Should().Be(60);
        allocations.Single(allocation => allocation.VendorId == second.VendorId)
            .DiscountAmount.Amount.Should().Be(40);
    }

    [Fact]
    public void Allocate_EqualRemainders_ShouldUseVendorIdTieBreaker()
    {
        var first = DiscountTestData.VendorAmount(1, 1);
        var second = DiscountTestData.VendorAmount(2, 1);
        var third = DiscountTestData.VendorAmount(3, 1);

        var allocations = _allocator.Allocate(
            DiscountTestData.Money(100),
            [third, first, second]).Value.ToArray();

        allocations.Select(allocation => allocation.VendorId)
            .Should().ContainInOrder(first.VendorId, second.VendorId, third.VendorId);
        allocations.Select(allocation => allocation.DiscountAmount.Amount)
            .Should().ContainInOrder(34, 33, 33);
    }

    [Fact]
    public void Allocate_ShouldPreserveExactTotalAndVendorCapsForValidDiscount()
    {
        var vendorAmounts = new[]
        {
            DiscountTestData.VendorAmount(1, 5),
            DiscountTestData.VendorAmount(2, 7),
            DiscountTestData.VendorAmount(3, 11)
        };

        var allocations = _allocator.Allocate(
            DiscountTestData.Money(17),
            vendorAmounts).Value;

        allocations.Sum(allocation => allocation.DiscountAmount.Amount)
            .Should().Be(17);
        allocations.Should().OnlyContain(allocation =>
            allocation.DiscountAmount.Amount
            <= vendorAmounts.Single(amount =>
                amount.VendorId == allocation.VendorId).ProductsAmount.Amount);
    }

    [Fact]
    public void Allocate_ZeroDiscount_ShouldReturnEmptyCollection()
    {
        _allocator.Allocate(
                DiscountTestData.Money(0),
                Array.Empty<VendorProductAmount>())
            .Value.Should().BeEmpty();
    }

    [Fact]
    public void Allocate_OneVendor_ShouldReceiveCompleteDiscount()
    {
        var vendor = DiscountTestData.VendorAmount(1, 1_000);

        _allocator.Allocate(DiscountTestData.Money(333), [vendor])
            .Value.Should().ContainSingle()
            .Which.DiscountAmount.Amount.Should().Be(333);
    }

    [Fact]
    public void Allocate_InputOrderShouldNotAffectOutput()
    {
        var first = DiscountTestData.VendorAmount(1, 600);
        var second = DiscountTestData.VendorAmount(2, 400);

        var forward = _allocator.Allocate(
            DiscountTestData.Money(101),
            [first, second]).Value;
        var reverse = _allocator.Allocate(
            DiscountTestData.Money(101),
            [second, first]).Value;

        reverse.Should().Equal(forward);
        forward.Select(allocation => allocation.VendorId.Value)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void Allocate_ShouldUseBigIntegerForLargeIntermediateProducts()
    {
        var first = new VendorProductAmount(
            DiscountTestData.Vendor(1),
            DiscountTestData.Money(long.MaxValue));
        var second = new VendorProductAmount(
            DiscountTestData.Vendor(2),
            DiscountTestData.Money(long.MaxValue));

        var result = _allocator.Allocate(
            DiscountTestData.Money(long.MaxValue),
            [first, second]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sum(allocation => (decimal)allocation.DiscountAmount.Amount)
            .Should().Be(long.MaxValue);
    }

    [Fact]
    public void Allocate_PositiveDiscountWithZeroEligibleTotal_ShouldFail()
    {
        var result = _allocator.Allocate(
            DiscountTestData.Money(1),
            [DiscountTestData.VendorAmount(1, 0)]);

        result.Error.Code.Should().Be("discount.allocation_failed");
    }

    [Fact]
    public void Allocate_ShouldBeRepeatableAndReturnReadOnlyOutput()
    {
        var amounts = new[]
        {
            DiscountTestData.VendorAmount(2, 400),
            DiscountTestData.VendorAmount(1, 600)
        };

        var first = _allocator.Allocate(
            DiscountTestData.Money(101),
            amounts).Value;
        var second = _allocator.Allocate(
            DiscountTestData.Money(101),
            amounts).Value;

        second.Should().Equal(first);
        var mutation = () =>
            ((ICollection<VendorDiscountAllocation>)first).Clear();
        mutation.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Allocate_ThreeHundredGeneratedScenarios_ShouldPreserveInvariants()
    {
        var random = new Random(12345);

        for (var scenario = 0; scenario < 300; scenario++)
        {
            var vendorCount = random.Next(1, 9);
            var amounts = Enumerable.Range(1, vendorCount)
                .Select(index => DiscountTestData.VendorAmount(
                    scenario * 10 + index,
                    random.NextInt64(1, 1_000_001)))
                .ToArray();
            var eligibleTotal = amounts.Sum(amount => amount.ProductsAmount.Amount);
            var totalDiscount = random.NextInt64(0, eligibleTotal + 1);

            var firstResult = _allocator.Allocate(
                DiscountTestData.Money(totalDiscount),
                amounts);
            var secondResult = _allocator.Allocate(
                DiscountTestData.Money(totalDiscount),
                amounts.Reverse().ToArray());

            firstResult.IsSuccess.Should().BeTrue();
            secondResult.Value.Should().Equal(firstResult.Value);

            var allocations = firstResult.Value;
            allocations.Sum(allocation => allocation.DiscountAmount.Amount)
                .Should().Be(totalDiscount);
            allocations.Should().OnlyContain(allocation =>
                allocation.DiscountAmount.Amount >= 0);
            allocations.Should().OnlyContain(allocation =>
                amounts.Any(amount => amount.VendorId == allocation.VendorId));
            allocations.Should().OnlyContain(allocation =>
                allocation.DiscountAmount.Amount
                <= amounts.Single(amount =>
                    amount.VendorId == allocation.VendorId).ProductsAmount.Amount);
            allocations.Select(allocation => allocation.VendorId.Value)
                .Should().BeInAscendingOrder();
        }
    }
}

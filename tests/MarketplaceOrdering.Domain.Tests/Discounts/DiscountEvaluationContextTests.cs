using FluentAssertions;
using MarketplaceOrdering.Domain.Discounts;

namespace MarketplaceOrdering.Domain.Tests.Discounts;

public sealed class DiscountEvaluationContextTests
{
    [Fact]
    public void Create_WithOneVendor_ShouldSucceed()
    {
        var context = DiscountEvaluationContext.Create(
            DiscountTestData.Money(100),
            [DiscountTestData.VendorAmount(1, 100)],
            DiscountTestData.EvaluatedAt).Value;

        context.TotalProductsAmount.Amount.Should().Be(100);
        context.VendorAmounts.Should().ContainSingle();
        context.EvaluatedAt.Should().Be(DiscountTestData.EvaluatedAt);
    }

    [Fact]
    public void Create_WithMultipleVendorsAndMatchingTotal_ShouldSucceed()
    {
        DiscountEvaluationContext.Create(
            DiscountTestData.Money(300),
            [
                DiscountTestData.VendorAmount(1, 100),
                DiscountTestData.VendorAmount(2, 200)
            ],
            DiscountTestData.EvaluatedAt).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WithNullOrEmptyVendors_ShouldFail()
    {
        DiscountEvaluationContext.Create(
            DiscountTestData.Money(0),
            null,
            DiscountTestData.EvaluatedAt).Error.Code
            .Should().Be("discount.vendor_amounts_required");
        DiscountEvaluationContext.Create(
            DiscountTestData.Money(0),
            Array.Empty<VendorProductAmount>(),
            DiscountTestData.EvaluatedAt).Error.Code
            .Should().Be("discount.vendor_amounts_required");
    }

    [Fact]
    public void Create_WithDuplicateVendor_ShouldFail()
    {
        var vendor = DiscountTestData.Vendor(1);

        var result = DiscountEvaluationContext.Create(
            DiscountTestData.Money(200),
            [
                new VendorProductAmount(vendor, DiscountTestData.Money(100)),
                new VendorProductAmount(vendor, DiscountTestData.Money(100))
            ],
            DiscountTestData.EvaluatedAt);

        result.Error.Code.Should().Be("discount.duplicate_vendor");
        result.Error.Metadata.Should().Contain("vendorId", vendor.ToString());
    }

    [Theory]
    [InlineData(99)]
    [InlineData(101)]
    public void Create_WithInconsistentVendorSum_ShouldFail(long total)
    {
        var result = DiscountEvaluationContext.Create(
            DiscountTestData.Money(total),
            [DiscountTestData.VendorAmount(1, 100)],
            DiscountTestData.EvaluatedAt);

        result.Error.Code.Should().Be(
            "discount.inconsistent_total_products_amount");
        result.Error.Metadata.Should().Contain(
            "expectedAmount",
            total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        result.Error.Metadata.Should().Contain("actualAmount", "100");
    }

    [Fact]
    public void Create_ShouldNormalizeVendorOrder()
    {
        var first = DiscountTestData.VendorAmount(1, 100);
        var second = DiscountTestData.VendorAmount(2, 200);

        var context = DiscountEvaluationContext.Create(
            DiscountTestData.Money(300),
            [second, first],
            DiscountTestData.EvaluatedAt).Value;

        context.VendorAmounts.Select(amount => amount.VendorId)
            .Should().ContainInOrder(first.VendorId, second.VendorId);
    }

    [Fact]
    public void VendorAmounts_ShouldBeReadOnlyAndDefensivelyCopied()
    {
        var source = new[]
        {
            DiscountTestData.VendorAmount(1, 100)
        };
        var context = DiscountEvaluationContext.Create(
            DiscountTestData.Money(100),
            source,
            DiscountTestData.EvaluatedAt).Value;
        source[0] = DiscountTestData.VendorAmount(2, 100);

        context.VendorAmounts.Single().VendorId.Should()
            .Be(DiscountTestData.Vendor(1));
        var mutation = () =>
            ((ICollection<VendorProductAmount>)context.VendorAmounts).Clear();
        mutation.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Create_WhenVendorSumOverflows_ShouldReturnCalculationOverflow()
    {
        var result = DiscountEvaluationContext.Create(
            DiscountTestData.Money(long.MaxValue),
            [
                DiscountTestData.VendorAmount(1, long.MaxValue),
                DiscountTestData.VendorAmount(2, 1)
            ],
            DiscountTestData.EvaluatedAt);

        result.Error.Code.Should().Be("discount.calculation_overflow");
    }
}

using System.Reflection;
using FluentAssertions;
using MarketplaceOrdering.Domain.Fulfillment;

namespace MarketplaceOrdering.Domain.Tests.Fulfillment;

public sealed class FulfillmentSnapshotTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    public void Offer_Create_ShouldAcceptValidDeliveryHours(int hours)
    {
        FulfillmentTestData.Offer(1, 1, 0, 0, 0, 0, hours)
            .EstimatedDeliveryHours.Should().Be(hours);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Offer_Create_ShouldRejectInvalidDeliveryHours(int hours)
    {
        ProductOffer.Create(FulfillmentTestData.Vendor(1),
            FulfillmentTestData.Product(1).ProductId,
            FulfillmentTestData.Money(1), 1, FulfillmentTestData.Money(0),
            FulfillmentTestData.Money(0), hours).Error.Code
            .Should().Be("fulfillment.invalid_delivery_hours");
    }

    [Fact]
    public void Allocation_Create_ShouldCalculateLineTotalAndPreserveSnapshot()
    {
        var product = FulfillmentTestData.Product(1);
        var allocation = ProductAllocation.Create(
            FulfillmentTestData.Vendor(1), product.ProductId,
            product.ProductName, FulfillmentTestData.Quantity(3),
            FulfillmentTestData.Money(25), 12).Value;

        allocation.LineTotal.Amount.Should().Be(75);
        allocation.ProductName.Should().Be(product.ProductName);
        allocation.Quantity.Value.Should().Be(3);
        allocation.EstimatedDeliveryHours.Should().Be(12);
    }

    [Fact]
    public void Allocation_Create_ShouldRejectZeroPriceAndOverflow()
    {
        var product = FulfillmentTestData.Product(1);
        ProductAllocation.Create(FulfillmentTestData.Vendor(1), product.ProductId,
            product.ProductName, FulfillmentTestData.Quantity(1),
            FulfillmentTestData.Money(0), 1).Error.Code
            .Should().Be("fulfillment.invalid_allocation");
        ProductAllocation.Create(FulfillmentTestData.Vendor(1), product.ProductId,
            product.ProductName, FulfillmentTestData.Quantity(2),
            FulfillmentTestData.Money(long.MaxValue), 1).Error.Code
            .Should().Be("fulfillment.calculation_overflow");
    }

    [Fact]
    public void PublicFulfillmentModels_ShouldHaveNoPublicSetters()
    {
        foreach (var type in new[]
                 { typeof(ProductOffer), typeof(ProductAllocation),
                   typeof(VendorFulfillment), typeof(FulfillmentPlan) })
        {
            foreach (var property in type.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                (property.SetMethod == null || !property.SetMethod.IsPublic)
                    .Should().BeTrue();
            }
        }
    }
}

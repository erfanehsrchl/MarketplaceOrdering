using FluentAssertions;
using MarketplaceOrdering.Domain.Discounts;

namespace MarketplaceOrdering.Domain.Tests.Discounts;

public sealed class DiscountValueTests
{
    [Theory]
    [InlineData(0.01)]
    [InlineData(10)]
    [InlineData(30)]
    public void Percentage_Create_ShouldAcceptValidValues(double input)
    {
        var percentage = (decimal)input;

        PercentageDiscountValue.Create(percentage).Value.Percentage
            .Should().Be(percentage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Percentage_Create_ShouldRejectNonPositiveValues(double input)
    {
        PercentageDiscountValue.Create((decimal)input).Error.Code
            .Should().Be("discount.percentage_not_positive");
    }

    [Fact]
    public void Percentage_Create_ShouldRejectValuesAboveThirty()
    {
        PercentageDiscountValue.Create(30.0001m).Error.Code
            .Should().Be("discount.percentage_exceeds_maximum");
    }

    [Fact]
    public void Percentage_Create_ShouldPreserveDecimalPrecision()
    {
        PercentageDiscountValue.Create(12.3456789m).Value.Percentage
            .Should().Be(12.3456789m);
    }

    [Fact]
    public void Fixed_Create_ShouldPreservePositiveAmount()
    {
        var money = DiscountTestData.Money(123);

        FixedDiscountValue.Create(money).Value.Amount.Should().Be(money);
    }

    [Fact]
    public void Fixed_Create_ShouldRejectZero()
    {
        FixedDiscountValue.Create(DiscountTestData.Money(0)).Error.Code
            .Should().Be("discount.fixed_amount_not_positive");
    }
}

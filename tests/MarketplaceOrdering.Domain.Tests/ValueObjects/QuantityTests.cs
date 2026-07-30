using FluentAssertions;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.ValueObjects;

public sealed class QuantityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    public void Create_ShouldAcceptAnyPositiveValue(int input)
    {
        var result = Quantity.Create(input);

        result.Value.Value.Should().Be(input);
        result.Value.ToString().Should().Be(input.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldRejectNonPositiveValues(int input)
    {
        Quantity.Create(input).Error.Code.Should().Be("quantity.not_positive");
    }

    [Fact]
    public void Equality_ShouldUseUnderlyingValue()
    {
        Quantity.Create(2).Value.Should().Be(Quantity.Create(2).Value)
            .And.NotBe(Quantity.Create(3).Value);
    }
}

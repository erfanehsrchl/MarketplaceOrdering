using FluentAssertions;
using MarketplaceOrdering.Domain.Money;

namespace MarketplaceOrdering.Domain.Tests.Money;

public sealed class CurrencyTests
{
    [Fact]
    public void Create_ShouldAcceptValidInputAndNormalizeCode()
    {
        var currency = Currency.Create(" usd ", 2);

        currency.IsSuccess.Should().BeTrue();
        currency.Value.Code.Should().Be("USD");
        currency.Value.Scale.Should().Be(2);
        currency.Value.ToString().Should().Be("USD");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U1D")]
    [InlineData("UŚD")]
    public void Create_ShouldRejectInvalidCodes(string? code)
    {
        Currency.Create(code, 2).Error.Code.Should().Be("currency.invalid_code");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Create_ShouldRejectInvalidScales(int scale)
    {
        Currency.Create("USD", scale).Error.Code.Should().Be("currency.invalid_scale");
    }

    [Fact]
    public void PredefinedCurrencies_ShouldHaveExpectedCodeAndScale()
    {
        Currency.IRR.Should().Be(Currency.Create("IRR", 0).Value);
        Currency.USD.Should().Be(Currency.Create("USD", 2).Value);
        Currency.EUR.Should().Be(Currency.Create("EUR", 2).Value);
        Currency.JPY.Should().Be(Currency.Create("JPY", 0).Value);
        Currency.KWD.Should().Be(Currency.Create("KWD", 3).Value);
    }

    [Fact]
    public void Equality_ShouldIncludeScale()
    {
        Currency.Create("USD", 2).Value.Should().NotBe(Currency.Create("USD", 3).Value);
    }
}

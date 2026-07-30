using FluentAssertions;
using MarketplaceOrdering.Domain.Money;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Tests.Money;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(long.MaxValue)]
    public void Create_ShouldPreserveEveryNonNegativeMinorUnit(long minorUnits)
    {
        var money = MoneyValue.Create(minorUnits, Currency.USD);

        money.IsSuccess.Should().BeTrue();
        money.Value.MinorUnits.Should().Be(minorUnits);
    }

    [Fact]
    public void Create_ShouldRejectNegativeAmount()
    {
        MoneyValue.Create(-1, Currency.USD).Error.Code.Should().Be("money.negative");
    }

    [Fact]
    public void Zero_ShouldCreateNonNegativeZero()
    {
        var zero = MoneyValue.Zero(Currency.USD);

        zero.IsZero.Should().BeTrue();
        zero.MinorUnits.Should().Be(0);
        zero.Currency.Should().Be(Currency.USD);
    }

    [Fact]
    public void Add_ShouldAddMatchingCurrencies()
    {
        var left = MoneyValue.Create(100, Currency.USD).Value;
        var right = MoneyValue.Create(25, Currency.USD).Value;

        left.Add(right).Value.MinorUnits.Should().Be(125);
    }

    [Fact]
    public void Add_ShouldRejectDifferentCurrencies()
    {
        var result = MoneyValue.Create(100, Currency.USD).Value
            .Add(MoneyValue.Create(25, Currency.EUR).Value);

        result.Error.Code.Should().Be("money.currency_mismatch");
    }

    [Fact]
    public void Add_ShouldReturnFailureOnOverflow()
    {
        var result = MoneyValue.Create(long.MaxValue, Currency.USD).Value
            .Add(MoneyValue.Create(1, Currency.USD).Value);

        result.Error.Code.Should().Be("money.overflow");
    }

    [Fact]
    public void Subtract_ShouldSubtractMatchingCurrencies()
    {
        var result = MoneyValue.Create(100, Currency.USD).Value
            .Subtract(MoneyValue.Create(25, Currency.USD).Value);

        result.Value.MinorUnits.Should().Be(75);
    }

    [Fact]
    public void Subtract_ShouldRejectInsufficientAmount()
    {
        var result = MoneyValue.Create(25, Currency.USD).Value
            .Subtract(MoneyValue.Create(100, Currency.USD).Value);

        result.Error.Code.Should().Be("money.insufficient_amount");
    }

    [Fact]
    public void Subtract_ShouldRejectDifferentCurrencies()
    {
        var result = MoneyValue.Create(100, Currency.USD).Value
            .Subtract(MoneyValue.Create(25, Currency.EUR).Value);

        result.Error.Code.Should().Be("money.currency_mismatch");
    }

    [Fact]
    public void CompareTo_ShouldCompareMatchingCurrencies()
    {
        var smaller = MoneyValue.Create(25, Currency.USD).Value;
        var larger = MoneyValue.Create(100, Currency.USD).Value;

        smaller.CompareTo(larger).Value.Should().BeNegative();
        larger.CompareTo(smaller).Value.Should().BePositive();
        smaller.CompareTo(smaller).Value.Should().Be(0);
    }

    [Fact]
    public void CompareTo_ShouldRejectDifferentCurrencies()
    {
        var result = MoneyValue.Create(25, Currency.USD).Value
            .CompareTo(MoneyValue.Create(25, Currency.EUR).Value);

        result.Error.Code.Should().Be("money.currency_mismatch");
    }

    [Fact]
    public void Equality_ShouldIncludeAmountAndCurrency()
    {
        var usd = MoneyValue.Create(100, Currency.USD).Value;

        usd.Should().Be(MoneyValue.Create(100, Currency.USD).Value);
        usd.Should().NotBe(MoneyValue.Create(100, Currency.EUR).Value);
        usd.ToString().Should().Be("100 USD");
    }
}

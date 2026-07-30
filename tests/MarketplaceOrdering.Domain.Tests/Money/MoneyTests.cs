using FluentAssertions;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Tests.Money;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(long.MaxValue)]
    public void Create_ShouldPreserveEveryPositiveIntegerAmount(long amount)
    {
        var result = MoneyValue.Create(amount);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(amount);
    }

    [Fact]
    public void Create_ShouldAcceptZero()
    {
        var result = MoneyValue.Create(0);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(0);
        result.Value.Amount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Create_ShouldRejectNegativeAmount()
    {
        var result = MoneyValue.Create(-1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("money.negative");
    }

    [Fact]
    public void Zero_ShouldHaveAnAmountOfZero()
    {
        MoneyValue.Zero.Amount.Should().Be(0);
        MoneyValue.Zero.Amount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Zero_ShouldAddAndSubtractSafely()
    {
        var amount = MoneyValue.Create(125).Value;

        MoneyValue.Zero.Add(amount).Value.Should().Be(amount);
        amount.Add(MoneyValue.Zero).Value.Should().Be(amount);
        MoneyValue.Zero.Subtract(MoneyValue.Zero).Value.Should().Be(MoneyValue.Zero);
    }

    [Fact]
    public void Equality_ShouldBeBasedOnlyOnAmount()
    {
        MoneyValue.Create(100).Value.Should().Be(MoneyValue.Create(100).Value);
        MoneyValue.Create(100).Value.Should().NotBe(MoneyValue.Create(101).Value);
    }

    [Fact]
    public void Add_ShouldAddAmountsExactly()
    {
        var left = MoneyValue.Create(100).Value;
        var right = MoneyValue.Create(25).Value;

        left.Add(right).Value.Amount.Should().Be(125);
    }

    [Fact]
    public void Add_ShouldPreserveZeroIdentity()
    {
        var amount = MoneyValue.Create(987_654_321).Value;

        amount.Add(MoneyValue.Zero).Value.Should().Be(amount);
        MoneyValue.Zero.Add(amount).Value.Should().Be(amount);
    }

    [Fact]
    public void Add_ShouldPreserveEveryIntegerMonetaryUnit()
    {
        var result = MoneyValue.Create(9_007_199_254_740_991).Value
            .Add(MoneyValue.Create(1).Value);

        result.Value.Amount.Should().Be(9_007_199_254_740_992);
    }

    [Fact]
    public void Add_ShouldReturnOverflowFailureWithoutThrowing()
    {
        var maximum = MoneyValue.Create(long.MaxValue).Value;
        var one = MoneyValue.Create(1).Value;
        var operation = () => maximum.Add(one);

        operation.Should().NotThrow();
        operation().Error.Code.Should().Be("money.overflow");
    }

    [Fact]
    public void Subtract_ShouldSubtractSmallerAmount()
    {
        var result = MoneyValue.Create(100).Value
            .Subtract(MoneyValue.Create(25).Value);

        result.Value.Amount.Should().Be(75);
    }

    [Fact]
    public void Subtract_ShouldReturnZeroForEqualAmounts()
    {
        var amount = MoneyValue.Create(100).Value;

        amount.Subtract(amount).Value.Should().Be(MoneyValue.Zero);
    }

    [Fact]
    public void Subtract_ShouldPreserveAmountWhenSubtractingZero()
    {
        var amount = MoneyValue.Create(100).Value;

        amount.Subtract(MoneyValue.Zero).Value.Should().Be(amount);
    }

    [Fact]
    public void Subtract_ShouldReturnInsufficientAmountWithoutThrowing()
    {
        var smaller = MoneyValue.Create(25).Value;
        var larger = MoneyValue.Create(100).Value;
        var operation = () => smaller.Subtract(larger);

        operation.Should().NotThrow();
        operation().Error.Code.Should().Be("money.insufficient_amount");
    }

    [Fact]
    public void CompareTo_ShouldCompareOnlyAmounts()
    {
        var smaller = MoneyValue.Create(25).Value;
        var larger = MoneyValue.Create(100).Value;
        var equal = MoneyValue.Create(25).Value;

        smaller.CompareTo(larger).Should().BeNegative();
        larger.CompareTo(smaller).Should().BePositive();
        smaller.CompareTo(equal).Should().Be(0);
    }
}

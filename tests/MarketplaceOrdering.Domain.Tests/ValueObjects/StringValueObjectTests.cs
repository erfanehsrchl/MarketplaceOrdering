using FluentAssertions;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.ValueObjects;

public sealed class StringValueObjectTests
{
    [Fact]
    public void ProductName_ShouldValidateTrimAndPreserveCasing()
    {
        ProductName.Create(null).IsFailure.Should().BeTrue();
        ProductName.Create("").IsFailure.Should().BeTrue();
        ProductName.Create("  ").IsFailure.Should().BeTrue();
        ProductName.Create(new string('a', ProductName.MaximumLength + 1)).IsFailure.Should().BeTrue();

        var maximum = ProductName.Create(new string('a', ProductName.MaximumLength));
        var value = ProductName.Create("  Mixed Case  ");

        maximum.IsSuccess.Should().BeTrue();
        value.Value.Value.Should().Be("Mixed Case");
        value.Value.ToString().Should().Be("Mixed Case");
        value.Value.Should().Be(ProductName.Create("Mixed Case").Value);
    }

    [Fact]
    public void DiscountCode_ShouldValidateTrimAndNormalizeInvariantly()
    {
        DiscountCode.Create(null).IsFailure.Should().BeTrue();
        DiscountCode.Create("").IsFailure.Should().BeTrue();
        DiscountCode.Create("  ").IsFailure.Should().BeTrue();
        DiscountCode.Create(new string('a', DiscountCode.MaximumLength + 1)).IsFailure.Should().BeTrue();

        DiscountCode.Create(new string('a', DiscountCode.MaximumLength)).IsSuccess.Should().BeTrue();
        var value = DiscountCode.Create("  summerSale  ").Value;

        value.Value.Should().Be("SUMMERSALE");
        value.ToString().Should().Be("SUMMERSALE");
        value.Should().Be(DiscountCode.Create("SUMMERSALE").Value);
    }

    [Fact]
    public void DeliveryAddress_ShouldValidateAndTrim()
    {
        DeliveryAddress.Create(null).IsFailure.Should().BeTrue();
        DeliveryAddress.Create("").IsFailure.Should().BeTrue();
        DeliveryAddress.Create("  ").IsFailure.Should().BeTrue();
        DeliveryAddress.Create(new string('a', DeliveryAddress.MaximumLength + 1)).IsFailure.Should().BeTrue();
        DeliveryAddress.Create(new string('a', DeliveryAddress.MaximumLength)).IsSuccess.Should().BeTrue();

        var value = DeliveryAddress.Create("  10 Main Street  ").Value;
        value.Value.Should().Be("10 Main Street");
        value.ToString().Should().Be("10 Main Street");
        value.Should().Be(DeliveryAddress.Create("10 Main Street").Value);
    }

    [Fact]
    public void CancellationReason_ShouldValidateAndTrim()
    {
        CancellationReason.Create(null).IsFailure.Should().BeTrue();
        CancellationReason.Create("").IsFailure.Should().BeTrue();
        CancellationReason.Create("  ").IsFailure.Should().BeTrue();
        CancellationReason.Create(new string('a', CancellationReason.MaximumLength + 1)).IsFailure.Should().BeTrue();
        CancellationReason.Create(new string('a', CancellationReason.MaximumLength)).IsSuccess.Should().BeTrue();

        var value = CancellationReason.Create("  Changed mind  ").Value;
        value.Value.Should().Be("Changed mind");
        value.ToString().Should().Be("Changed mind");
        value.Should().Be(CancellationReason.Create("Changed mind").Value);
    }

    [Fact]
    public void TransactionId_ShouldValidateAndTrim()
    {
        TransactionId.Create(null).IsFailure.Should().BeTrue();
        TransactionId.Create("").IsFailure.Should().BeTrue();
        TransactionId.Create("  ").IsFailure.Should().BeTrue();
        TransactionId.Create(new string('a', TransactionId.MaximumLength + 1)).IsFailure.Should().BeTrue();
        TransactionId.Create(new string('a', TransactionId.MaximumLength)).IsSuccess.Should().BeTrue();

        var value = TransactionId.Create("  tx-123  ").Value;
        value.Value.Should().Be("tx-123");
        value.ToString().Should().Be("tx-123");
        value.Should().Be(TransactionId.Create("tx-123").Value);
    }

    [Fact]
    public void IdempotencyKey_ShouldValidateAndTrim()
    {
        IdempotencyKey.Create(null).IsFailure.Should().BeTrue();
        IdempotencyKey.Create("").IsFailure.Should().BeTrue();
        IdempotencyKey.Create("  ").IsFailure.Should().BeTrue();
        IdempotencyKey.Create(new string('a', IdempotencyKey.MaximumLength + 1)).IsFailure.Should().BeTrue();
        IdempotencyKey.Create(new string('a', IdempotencyKey.MaximumLength)).IsSuccess.Should().BeTrue();

        var value = IdempotencyKey.Create("  request-123  ").Value;
        value.Value.Should().Be("request-123");
        value.ToString().Should().Be("request-123");
        value.Should().Be(IdempotencyKey.Create("request-123").Value);
    }
}

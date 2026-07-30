using FluentAssertions;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.ValueObjects;

public sealed class IdentifierTests
{
    [Fact]
    public void OrderId_ShouldValidateAndUseValueEquality()
    {
        var value = Guid.NewGuid();
        var same = OrderId.Create(value);
        var different = OrderId.Create(Guid.NewGuid());

        same.IsSuccess.Should().BeTrue();
        OrderId.Create(Guid.Empty).Error.Code.Should().Be("order_id.empty");
        same.Value.Should().Be(OrderId.Create(value).Value).And.NotBe(different.Value);
        same.Value.ToString().Should().Be(value.ToString("D"));
    }

    [Fact]
    public void CustomerId_ShouldValidateAndUseValueEquality()
    {
        var value = Guid.NewGuid();
        var same = CustomerId.Create(value);
        var different = CustomerId.Create(Guid.NewGuid());

        same.IsSuccess.Should().BeTrue();
        CustomerId.Create(Guid.Empty).Error.Code.Should().Be("customer_id.empty");
        same.Value.Should().Be(CustomerId.Create(value).Value).And.NotBe(different.Value);
        same.Value.ToString().Should().Be(value.ToString("D"));
    }

    [Fact]
    public void ProductId_ShouldValidateAndUseValueEquality()
    {
        var value = Guid.NewGuid();
        var same = ProductId.Create(value);
        var different = ProductId.Create(Guid.NewGuid());

        same.IsSuccess.Should().BeTrue();
        ProductId.Create(Guid.Empty).Error.Code.Should().Be("product_id.empty");
        same.Value.Should().Be(ProductId.Create(value).Value).And.NotBe(different.Value);
        same.Value.ToString().Should().Be(value.ToString("D"));
    }

    [Fact]
    public void VendorId_ShouldValidateAndUseValueEquality()
    {
        var value = Guid.NewGuid();
        var same = VendorId.Create(value);
        var different = VendorId.Create(Guid.NewGuid());

        same.IsSuccess.Should().BeTrue();
        VendorId.Create(Guid.Empty).Error.Code.Should().Be("vendor_id.empty");
        same.Value.Should().Be(VendorId.Create(value).Value).And.NotBe(different.Value);
        same.Value.ToString().Should().Be(value.ToString("D"));
    }

    [Fact]
    public void ReservationId_ShouldValidateAndUseValueEquality()
    {
        var value = Guid.NewGuid();
        var same = ReservationId.Create(value);
        var different = ReservationId.Create(Guid.NewGuid());

        same.IsSuccess.Should().BeTrue();
        ReservationId.Create(Guid.Empty).Error.Code.Should().Be("reservation_id.empty");
        same.Value.Should().Be(ReservationId.Create(value).Value).And.NotBe(different.Value);
        same.Value.ToString().Should().Be(value.ToString("D"));
    }

    [Fact]
    public void CheckoutAttemptId_ShouldValidateAndUseValueEquality()
    {
        var value = Guid.NewGuid();
        var same = CheckoutAttemptId.Create(value);
        var different = CheckoutAttemptId.Create(Guid.NewGuid());

        same.IsSuccess.Should().BeTrue();
        CheckoutAttemptId.Create(Guid.Empty).Error.Code.Should().Be("checkout_attempt_id.empty");
        same.Value.Should().Be(CheckoutAttemptId.Create(value).Value).And.NotBe(different.Value);
        same.Value.ToString().Should().Be(value.ToString("D"));
    }

    [Fact]
    public void DomainGeneratedIdentifiers_ShouldBeNonEmpty()
    {
        OrderId.New().Value.Should().NotBeEmpty();
        ReservationId.New().Value.Should().NotBeEmpty();
        CheckoutAttemptId.New().Value.Should().NotBeEmpty();
    }
}

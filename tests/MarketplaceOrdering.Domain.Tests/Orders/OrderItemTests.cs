using System.Reflection;
using FluentAssertions;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Tests.TestFixtures;

namespace MarketplaceOrdering.Domain.Tests.Orders;

public sealed class OrderItemTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Create_ShouldAcceptQuantityUpToMaximum(int quantity)
    {
        var result = OrderItem.Create(
            OrderTestData.Product(1),
            OrderTestData.Quantity(quantity));

        result.Value.Quantity.Value.Should().Be(quantity);
    }

    [Fact]
    public void Create_ShouldRejectQuantityAboveMaximum()
    {
        var result = OrderItem.Create(
            OrderTestData.Product(1),
            OrderTestData.Quantity(11));

        result.Error.Code.Should().Be("order.quantity_limit_exceeded");
    }

    [Fact]
    public void Create_ShouldRejectDefaultNonPositiveQuantity()
    {
        var result = OrderItem.Create(OrderTestData.Product(1), default);

        result.Error.Code.Should().Be("quantity.not_positive");
    }

    [Fact]
    public void Increase_ShouldAllowResultOfTen()
    {
        var item = OrderItem.Create(
            OrderTestData.Product(1),
            OrderTestData.Quantity(6)).Value;

        var result = item.IncreaseQuantity(OrderTestData.Quantity(4));

        result.IsSuccess.Should().BeTrue();
        item.Quantity.Value.Should().Be(10);
    }

    [Fact]
    public void Increase_AboveTen_ShouldFailWithoutChangingQuantity()
    {
        var item = OrderItem.Create(
            OrderTestData.Product(1),
            OrderTestData.Quantity(6)).Value;

        var result = item.IncreaseQuantity(OrderTestData.Quantity(5));

        result.Error.Code.Should().Be("order.quantity_limit_exceeded");
        item.Quantity.Value.Should().Be(6);
    }

    [Fact]
    public void Change_ShouldAllowTenAndRejectAboveTenWithoutMutation()
    {
        var item = OrderItem.Create(
            OrderTestData.Product(1),
            OrderTestData.Quantity(1)).Value;

        item.ChangeQuantity(OrderTestData.Quantity(10)).IsSuccess.Should().BeTrue();
        var failure = item.ChangeQuantity(OrderTestData.Quantity(11));

        failure.Error.Code.Should().Be("order.quantity_limit_exceeded");
        item.Quantity.Value.Should().Be(10);
    }

    [Fact]
    public void PublicState_ShouldHaveNoPublicSettersOrMutationMethods()
    {
        var properties = typeof(OrderItem).GetProperties();

        properties.Single(property => property.Name == nameof(OrderItem.ProductId))
            .SetMethod.Should().BeNull();
        properties.Single(property => property.Name == nameof(OrderItem.ProductName))
            .SetMethod.Should().BeNull();
        properties.Single(property => property.Name == nameof(OrderItem.Quantity))
            .SetMethod!.IsPublic.Should().BeFalse();

        typeof(OrderItem)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Should().BeEmpty();
    }
}

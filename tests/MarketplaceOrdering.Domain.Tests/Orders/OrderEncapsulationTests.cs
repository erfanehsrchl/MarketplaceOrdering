using System.Reflection;
using FluentAssertions;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Tests.TestFixtures;

namespace MarketplaceOrdering.Domain.Tests.Orders;

public sealed class OrderEncapsulationTests
{
    [Fact]
    public void Items_ShouldNotExposeMutableAggregateCollection()
    {
        var order = OrderTestData.CreateOrder(
            OrderTestData.Initial(1),
            OrderTestData.Initial(2));
        var originalProductIds = order.Items.Select(item => item.ProductId).ToArray();
        var returnedItems = order.Items;

        returnedItems.Should().BeAssignableTo<OrderItem[]>();
        var returnedArray = (OrderItem[])returnedItems;
        returnedArray[0] = returnedArray[1];

        order.Items.Select(item => item.ProductId).Should().Equal(originalProductIds);
    }

    [Fact]
    public void Order_ShouldDirectlyOwnPrivateItemList()
    {
        var field = typeof(Order).GetField(
            "_items",
            BindingFlags.NonPublic | BindingFlags.Instance);

        field.Should().NotBeNull();
        field!.FieldType.Should().Be(typeof(List<OrderItem>));
        field.IsPrivate.Should().BeTrue();
        field.IsInitOnly.Should().BeTrue();
        typeof(Order).GetProperty(nameof(Order.Items))!
            .SetMethod.Should().BeNull();
    }

    [Fact]
    public void RemovedCollectionAbstraction_ShouldNotExist()
    {
        var removedTypeName =
            "MarketplaceOrdering.Domain.Orders.Order" + "Items";

        typeof(Order).Assembly.GetType(removedTypeName).Should().BeNull();
    }

    [Fact]
    public void OrderState_ShouldHaveNoPublicSetters()
    {
        var properties = typeof(Order).GetProperties();
        var protectedProperties = new[]
        {
            nameof(Order.Status),
            nameof(Order.CustomerId),
            nameof(Order.DeliveryAddress),
            nameof(Order.CreatedAt),
            nameof(Order.SelectedDiscount)
        };

        foreach (var propertyName in protectedProperties)
        {
            var setter = properties.Single(property => property.Name == propertyName).SetMethod;
            (setter is null || !setter.IsPublic).Should().BeTrue(
                $"{propertyName} must not have a public setter");
        }
    }

    [Fact]
    public void OrderItem_ShouldContainNeitherPriceNorVendorAssignment()
    {
        var propertyNames = typeof(OrderItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        propertyNames.Should().NotContain("Price");
        propertyNames.Should().NotContain("VendorId");
    }

    [Fact]
    public void OrderItemMutationMethods_ShouldNotBePublic()
    {
        typeof(OrderItem).GetMethod(
            "IncreaseQuantity",
            BindingFlags.Public | BindingFlags.Instance).Should().BeNull();
        typeof(OrderItem).GetMethod(
            "ChangeQuantity",
            BindingFlags.Public | BindingFlags.Instance).Should().BeNull();
    }
}

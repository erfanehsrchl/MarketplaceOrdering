using FluentAssertions;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Tests.TestFixtures;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Orders;

public sealed class OrderItemManagementTests
{
    [Fact]
    public void AddItem_NewProduct_ShouldAppendItemAndPreserveOrder()
    {
        var initial = OrderTestData.Initial(1);
        var added = OrderTestData.Product(2);
        var order = OrderTestData.CreateOrder(initial);
        order.ClearCommittedDomainEvents();

        var result = order.AddItem(added, OrderTestData.Quantity(2), OrderTestData.CreatedAt);

        result.IsSuccess.Should().BeTrue();
        order.Items.Select(item => item.ProductId)
            .Should().ContainInOrder(initial.Product.ProductId, added.ProductId);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderItemAddedDomainEvent>();
    }

    [Fact]
    public void AddItem_ExistingProduct_ShouldIncreaseWithoutDuplicateAndPreserveName()
    {
        var original = OrderTestData.Initial(1, 3, "Original");
        var order = OrderTestData.CreateOrder(original);
        var renamedReference = new ProductReference(
            original.Product.ProductId,
            ProductName.Create("Changed").Value);
        order.ClearCommittedDomainEvents();

        var result = order.AddItem(
            renamedReference,
            OrderTestData.Quantity(2),
            OrderTestData.CreatedAt);

        result.IsSuccess.Should().BeTrue();
        order.Items.Should().ContainSingle();
        order.Items.Single().Quantity.Value.Should().Be(5);
        order.Items.Single().ProductName.Value.Should().Be("Original");
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderItemQuantityIncreasedDomainEvent>();
    }

    [Fact]
    public void AddItem_ResultingQuantityOfTen_ShouldSucceed()
    {
        var initial = OrderTestData.Initial(1, 6);
        var order = OrderTestData.CreateOrder(initial);

        order.AddItem(
            initial.Product,
            OrderTestData.Quantity(4),
            OrderTestData.CreatedAt).IsSuccess.Should().BeTrue();

        order.Items.Single().Quantity.Value.Should().Be(10);
    }

    [Fact]
    public void AddItem_AboveTen_ShouldFailWithoutMutationOrEvent()
    {
        var initial = OrderTestData.Initial(1, 6);
        var order = OrderTestData.CreateOrder(initial);
        order.ClearCommittedDomainEvents();

        var result = order.AddItem(
            initial.Product,
            OrderTestData.Quantity(5),
            OrderTestData.CreatedAt);

        result.Error.Code.Should().Be("order.quantity_limit_exceeded");
        result.Error.Metadata.Should().Contain("productId", initial.Product.ProductId.ToString());
        result.Error.Metadata.Should().Contain("requestedQuantity", "11");
        result.Error.Metadata.Should().Contain("maximumQuantity", "10");
        order.Items.Single().Quantity.Value.Should().Be(6);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangeItemQuantity_ShouldChangeExistingItemToTen()
    {
        var initial = OrderTestData.Initial(1, 2);
        var order = OrderTestData.CreateOrder(initial);
        order.ClearCommittedDomainEvents();

        var result = order.ChangeItemQuantity(
            initial.Product.ProductId,
            OrderTestData.Quantity(10),
            OrderTestData.CreatedAt);

        result.IsSuccess.Should().BeTrue();
        order.Items.Single().Quantity.Value.Should().Be(10);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderItemQuantityChangedDomainEvent>();
    }

    [Fact]
    public void ChangeItemQuantity_AboveTen_ShouldFailWithoutMutationOrEvent()
    {
        var initial = OrderTestData.Initial(1, 2);
        var order = OrderTestData.CreateOrder(initial);
        order.ClearCommittedDomainEvents();

        var result = order.ChangeItemQuantity(
            initial.Product.ProductId,
            OrderTestData.Quantity(11),
            OrderTestData.CreatedAt);

        result.Error.Code.Should().Be("order.quantity_limit_exceeded");
        order.Items.Single().Quantity.Value.Should().Be(2);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangeItemQuantity_MissingProduct_ShouldFailWithoutMutationOrEvent()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1, 2));
        var missingId = ProductId.Create(Guid.NewGuid()).Value;
        order.ClearCommittedDomainEvents();

        var result = order.ChangeItemQuantity(
            missingId,
            OrderTestData.Quantity(3),
            OrderTestData.CreatedAt);

        result.Error.Code.Should().Be("order.product_not_found");
        result.Error.Metadata.Should().Contain("productId", missingId.ToString());
        order.Items.Single().Quantity.Value.Should().Be(2);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangeItemQuantity_SameQuantity_ShouldBeIdempotent()
    {
        var initial = OrderTestData.Initial(1, 2);
        var order = OrderTestData.CreateOrder(initial);
        order.ClearCommittedDomainEvents();

        var result = order.ChangeItemQuantity(
            initial.Product.ProductId,
            initial.Quantity,
            OrderTestData.CreatedAt);

        result.IsSuccess.Should().BeTrue();
        order.Items.Single().Quantity.Should().Be(initial.Quantity);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_FromMultipleItems_ShouldSucceedAndPreserveRemainingOrder()
    {
        var first = OrderTestData.Initial(1);
        var second = OrderTestData.Initial(2);
        var third = OrderTestData.Initial(3);
        var order = OrderTestData.CreateOrder(first, second, third);
        order.ClearCommittedDomainEvents();

        var result = order.RemoveItem(second.Product.ProductId, OrderTestData.CreatedAt);

        result.IsSuccess.Should().BeTrue();
        order.Items.Select(item => item.ProductId)
            .Should().ContainInOrder(first.Product.ProductId, third.Product.ProductId);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderItemRemovedDomainEvent>();
    }

    [Fact]
    public void RemoveItem_MissingProduct_ShouldFailWithoutMutationOrEvent()
    {
        var initial = OrderTestData.Initial(1);
        var order = OrderTestData.CreateOrder(initial, OrderTestData.Initial(2));
        var originalItems = order.Items.ToArray();
        order.ClearCommittedDomainEvents();

        var result = order.RemoveItem(
            ProductId.Create(Guid.NewGuid()).Value,
            OrderTestData.CreatedAt);

        result.Error.Code.Should().Be("order.product_not_found");
        order.Items.Should().Equal(originalItems);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RemoveItem_FinalItem_ShouldFailWithoutMutationOrEvent()
    {
        var initial = OrderTestData.Initial(1);
        var order = OrderTestData.CreateOrder(initial);
        order.ClearCommittedDomainEvents();

        var result = order.RemoveItem(initial.Product.ProductId, OrderTestData.CreatedAt);

        result.Error.Code.Should().Be("order.last_item_cannot_be_removed");
        order.Items.Should().ContainSingle();
        order.DomainEvents.Should().BeEmpty();
    }
}

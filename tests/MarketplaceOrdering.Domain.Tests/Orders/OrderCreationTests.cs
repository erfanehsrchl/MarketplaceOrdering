using FluentAssertions;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Tests.TestFixtures;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Orders;

public sealed class OrderCreationTests
{
    [Fact]
    public void Create_WithOneItem_ShouldStoreSuppliedValuesAndStartInDraft()
    {
        var orderId = OrderTestData.OrderId();
        var customerId = OrderTestData.CustomerId();
        var address = OrderTestData.Address();
        var createdAt = new DateTimeOffset(2026, 5, 1, 2, 3, 4, TimeSpan.Zero);

        var result = Order.Create(
            orderId,
            customerId,
            address,
            [OrderTestData.Initial(1, 2)],
            createdAt);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(orderId);
        result.Value.CustomerId.Should().Be(customerId);
        result.Value.DeliveryAddress.Should().Be(address);
        result.Value.Status.Should().Be(OrderStatus.Draft);
        result.Value.CreatedAt.Should().Be(createdAt);
        result.Value.Items.Should().ContainSingle();
    }

    [Fact]
    public void Create_WithNullItems_ShouldFailWithoutExposingAnOrder()
    {
        var result = Order.Create(
            OrderTestData.OrderId(),
            OrderTestData.CustomerId(),
            OrderTestData.Address(),
            null,
            OrderTestData.CreatedAt);

        result.Error.Code.Should().Be("order.items_required");
        var readValue = () => result.Value;
        readValue.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_WithEmptyItems_ShouldFail()
    {
        var result = Order.Create(
            OrderTestData.OrderId(),
            OrderTestData.CustomerId(),
            OrderTestData.Address(),
            Array.Empty<InitialOrderItem>(),
            OrderTestData.CreatedAt);

        result.Error.Code.Should().Be("order.items_required");
    }

    [Fact]
    public void Create_ShouldMergeDuplicatesByProductIdAndPreserveFirstName()
    {
        var first = OrderTestData.Initial(1, 2, "Original");
        var duplicate = new InitialOrderItem(
            new ProductReference(
                first.Product.ProductId,
                ProductName.Create("Replacement").Value),
            OrderTestData.Quantity(3));

        var order = OrderTestData.CreateOrder(first, duplicate);

        order.Items.Should().ContainSingle();
        order.Items.Single().Quantity.Value.Should().Be(5);
        order.Items.Single().ProductName.Value.Should().Be("Original");
    }

    [Fact]
    public void Create_ShouldPreserveFirstInsertionOrderWhenMergingDuplicates()
    {
        var first = OrderTestData.Initial(1, 2);
        var second = OrderTestData.Initial(2, 1);
        var duplicateFirst = new InitialOrderItem(
            first.Product,
            OrderTestData.Quantity(3));

        var order = OrderTestData.CreateOrder(first, second, duplicateFirst);

        order.Items.Select(item => item.ProductId)
            .Should().ContainInOrder(first.Product.ProductId, second.Product.ProductId);
    }

    [Fact]
    public void Create_WithMergedQuantityOfTen_ShouldSucceed()
    {
        var product = OrderTestData.Product(1);

        var result = Order.Create(
            OrderTestData.OrderId(),
            OrderTestData.CustomerId(),
            OrderTestData.Address(),
            [
                new InitialOrderItem(product, OrderTestData.Quantity(4)),
                new InitialOrderItem(product, OrderTestData.Quantity(6))
            ],
            OrderTestData.CreatedAt);

        result.Value.Items.Single().Quantity.Value.Should().Be(10);
    }

    [Fact]
    public void Create_WithMergedQuantityAboveTen_ShouldFailWithoutExposingEvents()
    {
        var product = OrderTestData.Product(1);

        var result = Order.Create(
            OrderTestData.OrderId(),
            OrderTestData.CustomerId(),
            OrderTestData.Address(),
            [
                new InitialOrderItem(product, OrderTestData.Quantity(6)),
                new InitialOrderItem(product, OrderTestData.Quantity(5))
            ],
            OrderTestData.CreatedAt);

        result.Error.Code.Should().Be("order.quantity_limit_exceeded");
        var readValue = () => result.Value.DomainEvents;
        readValue.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_ShouldRaiseOrderedEventsForDistinctFinalItems()
    {
        var first = OrderTestData.Initial(1, 1);
        var second = OrderTestData.Initial(2, 2);
        var duplicate = new InitialOrderItem(first.Product, OrderTestData.Quantity(3));

        var order = OrderTestData.CreateOrder(first, second, duplicate);

        order.DomainEvents.Should().HaveCount(3);
        order.DomainEvents.First().Should().BeOfType<OrderCreatedDomainEvent>();
        order.DomainEvents.Skip(1).Should().AllBeOfType<OrderItemAddedDomainEvent>();
        order.DomainEvents.Skip(1)
            .Cast<OrderItemAddedDomainEvent>()
            .Select(domainEvent => domainEvent.ProductId)
            .Should().ContainInOrder(first.Product.ProductId, second.Product.ProductId);
    }
}

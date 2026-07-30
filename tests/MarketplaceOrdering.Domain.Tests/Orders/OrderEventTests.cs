using FluentAssertions;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Tests.TestFixtures;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Orders;

public sealed class OrderEventTests
{
    [Fact]
    public void CreationEvents_ShouldContainExpectedPayloadTimeAndIdentifiers()
    {
        var orderId = OrderTestData.OrderId();
        var customerId = OrderTestData.CustomerId();
        var first = OrderTestData.Initial(1, 2);
        var second = OrderTestData.Initial(2, 3);

        var order = Order.Create(
            orderId,
            customerId,
            OrderTestData.Address(),
            [first, second],
            OrderTestData.CreatedAt).Value;

        var created = order.DomainEvents.First().Should()
            .BeOfType<OrderCreatedDomainEvent>().Which;
        created.OrderId.Should().Be(orderId);
        created.CustomerId.Should().Be(customerId);

        var addedEvents = order.DomainEvents.Skip(1)
            .Cast<OrderItemAddedDomainEvent>()
            .ToArray();
        addedEvents.Should().HaveCount(2);
        addedEvents[0].ProductId.Should().Be(first.Product.ProductId);
        addedEvents[0].ProductName.Should().Be(first.Product.ProductName);
        addedEvents[0].Quantity.Should().Be(first.Quantity);
        addedEvents[1].ProductId.Should().Be(second.Product.ProductId);
        order.DomainEvents.Should().OnlyContain(
            domainEvent => domainEvent.OccurredAt == OrderTestData.CreatedAt);
        order.DomainEvents.Should().OnlyContain(
            domainEvent => domainEvent.EventId != Guid.Empty);
    }

    [Fact]
    public void DuplicateInitialProducts_ShouldRaiseOneAddedEventWithMergedQuantity()
    {
        var product = OrderTestData.Product(1);
        var order = OrderTestData.CreateOrder(
            new InitialOrderItem(product, OrderTestData.Quantity(2)),
            new InitialOrderItem(product, OrderTestData.Quantity(3)));

        var added = order.DomainEvents.OfType<OrderItemAddedDomainEvent>()
            .Should().ContainSingle().Which;

        added.Quantity.Value.Should().Be(5);
    }

    [Fact]
    public void AddNewItemEvent_ShouldContainPayloadAndSuppliedTime()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1));
        var product = OrderTestData.Product(2);
        var quantity = OrderTestData.Quantity(4);
        var occurredAt = OrderTestData.CreatedAt.AddMinutes(1);
        order.ClearCommittedDomainEvents();

        order.AddItem(product, quantity, occurredAt);

        var domainEvent = order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderItemAddedDomainEvent>().Which;
        domainEvent.OrderId.Should().Be(order.Id);
        domainEvent.ProductId.Should().Be(product.ProductId);
        domainEvent.ProductName.Should().Be(product.ProductName);
        domainEvent.Quantity.Should().Be(quantity);
        domainEvent.OccurredAt.Should().Be(occurredAt);
        domainEvent.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public void IncreaseEvent_ShouldContainPreviousAddedAndResultingQuantities()
    {
        var initial = OrderTestData.Initial(1, 3);
        var order = OrderTestData.CreateOrder(initial);
        var occurredAt = OrderTestData.CreatedAt.AddMinutes(2);
        order.ClearCommittedDomainEvents();

        order.AddItem(initial.Product, OrderTestData.Quantity(4), occurredAt);

        var domainEvent = order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderItemQuantityIncreasedDomainEvent>().Which;
        domainEvent.PreviousQuantity.Value.Should().Be(3);
        domainEvent.AddedQuantity.Value.Should().Be(4);
        domainEvent.ResultingQuantity.Value.Should().Be(7);
        domainEvent.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public void ChangeEvent_ShouldContainPreviousAndNewQuantities()
    {
        var initial = OrderTestData.Initial(1, 3);
        var order = OrderTestData.CreateOrder(initial);
        var occurredAt = OrderTestData.CreatedAt.AddMinutes(3);
        order.ClearCommittedDomainEvents();

        order.ChangeItemQuantity(
            initial.Product.ProductId,
            OrderTestData.Quantity(8),
            occurredAt);

        var domainEvent = order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderItemQuantityChangedDomainEvent>().Which;
        domainEvent.PreviousQuantity.Value.Should().Be(3);
        domainEvent.NewQuantity.Value.Should().Be(8);
        domainEvent.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public void RemoveEvent_ShouldContainRemovedProductAndQuantity()
    {
        var removed = OrderTestData.Initial(1, 3);
        var order = OrderTestData.CreateOrder(removed, OrderTestData.Initial(2));
        var occurredAt = OrderTestData.CreatedAt.AddMinutes(4);
        order.ClearCommittedDomainEvents();

        order.RemoveItem(removed.Product.ProductId, occurredAt);

        var domainEvent = order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderItemRemovedDomainEvent>().Which;
        domainEvent.ProductId.Should().Be(removed.Product.ProductId);
        domainEvent.RemovedQuantity.Should().Be(removed.Quantity);
        domainEvent.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public void DiscountEvents_ShouldContainCodeAndOperationTimes()
    {
        var order = OrderTestData.CreateOrder(OrderTestData.Initial(1));
        var code = DiscountCode.Create("SAVE10").Value;
        var selectedAt = OrderTestData.CreatedAt.AddMinutes(5);
        var removedAt = OrderTestData.CreatedAt.AddMinutes(6);
        order.ClearCommittedDomainEvents();

        order.SelectDiscountCode(code, selectedAt);
        order.RemoveDiscountCode(removedAt);

        order.DomainEvents.Should().HaveCount(2);
        var selected = order.DomainEvents.First().Should()
            .BeOfType<DiscountCodeSelectedDomainEvent>().Which;
        selected.DiscountCode.Should().Be(code);
        selected.OccurredAt.Should().Be(selectedAt);
        var removed = order.DomainEvents.Last().Should()
            .BeOfType<DiscountCodeRemovedDomainEvent>().Which;
        removed.DiscountCode.Should().Be(code);
        removed.OccurredAt.Should().Be(removedAt);
        order.DomainEvents.Should().OnlyContain(
            domainEvent => domainEvent.EventId != Guid.Empty);
    }

    [Fact]
    public void SequentialOperations_ShouldKeepDeterministicEventOrder()
    {
        var first = OrderTestData.Initial(1, 2);
        var second = OrderTestData.Initial(2, 1);
        var order = OrderTestData.CreateOrder(first, second);
        order.ClearCommittedDomainEvents();

        order.AddItem(first.Product, OrderTestData.Quantity(1), OrderTestData.CreatedAt);
        order.ChangeItemQuantity(
            second.Product.ProductId,
            OrderTestData.Quantity(2),
            OrderTestData.CreatedAt);
        order.RemoveItem(first.Product.ProductId, OrderTestData.CreatedAt);

        order.DomainEvents.Select(domainEvent => domainEvent.GetType()).Should().ContainInOrder(
            typeof(OrderItemQuantityIncreasedDomainEvent),
            typeof(OrderItemQuantityChangedDomainEvent),
            typeof(OrderItemRemovedDomainEvent));
    }
}

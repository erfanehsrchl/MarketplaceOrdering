using FluentAssertions;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.AddOrderItem;
using MarketplaceOrdering.Application.Orders.ApplyDiscountCode;
using MarketplaceOrdering.Application.Orders.ChangeOrderItemQuantity;
using MarketplaceOrdering.Application.Orders.RemoveDiscountCode;
using MarketplaceOrdering.Application.Orders.RemoveOrderItem;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Orders.Events;

namespace MarketplaceOrdering.Application.Tests.Orders;

public sealed class OrderMutationHandlerTests
{
    [Fact]
    public async Task AddItem_ShouldLoadMutateAndSaveAtLoadedVersion()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = new FakeOrderRepository
        {
            LoadedOrder = ApplicationTestData.Persisted(order, 7)
        };
        var clock = new FakeClock();
        var occurredAt = clock.UtcNow;
        using var cancellation = new CancellationTokenSource();
        var command = new AddOrderItemCommand(
            order.Id.Value,
            Guid.Parse("90000000-0000-0000-0000-000000000000"),
            "Added",
            2);

        var result = await new AddOrderItemCommandHandler(repository, clock)
            .Handle(command, cancellation.Token);

        result.Value.Version.Should().Be(8);
        result.Value.Items.Should().HaveCount(2);
        repository.LoadCalls.Should().Be(1);
        repository.SaveCalls.Should().Be(1);
        repository.CapturedOrderVersion.Should().Be(7);
        repository.LoadCancellationToken.Should().Be(cancellation.Token);
        repository.SaveCancellationToken.Should().Be(cancellation.Token);
        repository.SavedDomainEvents.OfType<OrderItemAddedDomainEvent>().Last()
            .OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public async Task AddExistingProduct_ShouldDelegateMergeToDomain()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = RepositoryFor(order);
        var product = order.Items.Single();

        var result = await new AddOrderItemCommandHandler(
            repository, new FakeClock()).Handle(
                new AddOrderItemCommand(
                    order.Id.Value,
                    product.ProductId.Value,
                    "Replacement name",
                    2),
                CancellationToken.None);

        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().Quantity.Should().Be(3);
        result.Value.Items.Single().ProductName.Should().Be("Product 1");
    }

    [Fact]
    public async Task AddQuantityLimitFailure_ShouldNotSave()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = RepositoryFor(order);
        var item = order.Items.Single();

        var result = await new AddOrderItemCommandHandler(
            repository, new FakeClock()).Handle(
                new AddOrderItemCommand(
                    order.Id.Value,
                    item.ProductId.Value,
                    item.ProductName.Value,
                    10),
                CancellationToken.None);

        result.Error.Code.Should().Be("order.quantity_limit_exceeded");
        repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task ChangeQuantity_ShouldReturnNewVersion()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = RepositoryFor(order, 5);

        var result = await new ChangeOrderItemQuantityCommandHandler(
            repository, new FakeClock()).Handle(
                new ChangeOrderItemQuantityCommand(
                    order.Id.Value,
                    order.Items.Single().ProductId.Value,
                    4),
                CancellationToken.None);

        result.Value.Version.Should().Be(6);
        result.Value.Items.Single().Quantity.Should().Be(4);
        repository.CapturedOrderVersion.Should().Be(5);
    }

    [Fact]
    public async Task ChangeMissingProduct_ShouldNotSave()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = RepositoryFor(order);

        var result = await new ChangeOrderItemQuantityCommandHandler(
            repository, new FakeClock()).Handle(
                new ChangeOrderItemQuantityCommand(
                    order.Id.Value, Guid.NewGuid(), 2),
                CancellationToken.None);

        result.Error.Code.Should().Be("order.product_not_found");
        repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task RemoveItem_ShouldUseDomainFinalItemProtection()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = RepositoryFor(order);

        var result = await new RemoveOrderItemCommandHandler(
            repository, new FakeClock()).Handle(
                new RemoveOrderItemCommand(
                    order.Id.Value, order.Items.Single().ProductId.Value),
                CancellationToken.None);

        result.Error.Code.Should().Be("order.last_item_cannot_be_removed");
        repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task RemoveItem_ShouldSaveSuccessfulDomainMutation()
    {
        var order = ApplicationTestData.CreateOrder(2);
        var repository = RepositoryFor(order);
        var removed = order.Items.First().ProductId;

        var result = await new RemoveOrderItemCommandHandler(
            repository, new FakeClock()).Handle(
                new RemoveOrderItemCommand(order.Id.Value, removed.Value),
                CancellationToken.None);

        result.Value.Items.Should().ContainSingle();
        repository.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task ApplyAndRemoveDiscount_ShouldUseDomainBehavior()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = RepositoryFor(order);
        var clock = new FakeClock();

        var applied = await new ApplyDiscountCodeCommandHandler(
                repository, ApplicationTestData.DiscountProvider(), clock)
            .Handle(
                new ApplyDiscountCodeCommand(order.Id.Value, " save "),
                CancellationToken.None);

        applied.Value.SelectedDiscount!.Code.Should().Be("SAVE");
        repository.LoadedOrder = ApplicationTestData.Persisted(order, 5);
        var removed = await new RemoveDiscountCodeCommandHandler(repository, clock)
            .Handle(
                new RemoveDiscountCodeCommand(order.Id.Value),
                CancellationToken.None);

        removed.Value.SelectedDiscount.Should().BeNull();
        repository.SaveCalls.Should().Be(2);
    }

    [Fact]
    public async Task IdempotentDiscountRemoval_ShouldStillSave()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = RepositoryFor(order);

        var result = await new RemoveDiscountCodeCommandHandler(
            repository, new FakeClock()).Handle(
                new RemoveDiscountCodeCommand(order.Id.Value),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task MissingOrder_ShouldBeReturnedWithoutSave()
    {
        var repository = new FakeOrderRepository();

        var result = await new AddOrderItemCommandHandler(
            repository, new FakeClock()).Handle(
                new AddOrderItemCommand(
                    Guid.NewGuid(), Guid.NewGuid(), "Product", 1),
                CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.OrderNotFound);
        repository.LoadCalls.Should().Be(1);
        repository.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task SaveFailure_ShouldRemainFailureAndEventsPending()
    {
        var order = ApplicationTestData.CreateOrder();
        var repository = RepositoryFor(order);
        repository.SaveFailure = ApplicationErrors.OrderVersionConflict;

        var result = await new AddOrderItemCommandHandler(
            repository, new FakeClock()).Handle(
                new AddOrderItemCommand(
                    order.Id.Value, Guid.NewGuid(), "Product", 1),
                CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.OrderVersionConflict);
        repository.LoadCalls.Should().Be(1);
        repository.SaveCalls.Should().Be(1);
        order.DomainEvents.Should().ContainSingle();
        order.Version.Should().Be(4);
    }

    private static FakeOrderRepository RepositoryFor(
        MarketplaceOrdering.Domain.Orders.Order order,
        long version = 4) => new()
    {
        LoadedOrder = ApplicationTestData.Persisted(order, version)
    };
}

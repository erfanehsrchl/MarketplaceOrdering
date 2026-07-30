using FluentAssertions;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Persistence;

public sealed class InMemoryOrderRepositoryTests
{
    [Fact]
    public async Task AddAndLoadUseVersionOneAndCommitEvents()
    {
        var repository = new InMemoryOrderRepository();
        var order = InfrastructureTestData.Order();

        var added = await repository.AddAsync(order, default);
        var loaded = await repository.LoadAsync(order.Id, default);

        added.Value.Should().Be(1);
        order.DomainEvents.Should().BeEmpty();
        loaded.Value.Version.Should().Be(1);
        loaded.Value.Order.Should().NotBeSameAs(order);
        loaded.Value.Order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateAddFailsWithoutClearingEvents()
    {
        var repository = new InMemoryOrderRepository();
        var id = OrderId.New();
        await repository.AddAsync(InfrastructureTestData.Order(id), default);
        var duplicate = InfrastructureTestData.Order(id);

        var result = await repository.AddAsync(duplicate, default);

        result.Error.Code.Should().Be("order.already_exists");
        duplicate.DomainEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MissingLoadAndSaveReturnNotFound()
    {
        var repository = new InMemoryOrderRepository();
        var order = InfrastructureTestData.Order();

        (await repository.LoadAsync(order.Id, default))
            .Error.Code.Should().Be("order.not_found");
        (await repository.SaveAsync(order, 1, default))
            .Error.Code.Should().Be("order.not_found");
    }

    [Fact]
    public async Task SaveIsOptimisticAndConflictPreservesStateAndEvents()
    {
        var repository = new InMemoryOrderRepository();
        var order = InfrastructureTestData.Order();
        await repository.AddAsync(order, default);
        var first = (await repository.LoadAsync(order.Id, default)).Value.Order;
        var stale = (await repository.LoadAsync(order.Id, default)).Value.Order;
        first.ChangeItemQuantity(first.Items.Single().ProductId,
            Quantity.Create(2).Value, InfrastructureTestData.Now);
        stale.ChangeItemQuantity(stale.Items.Single().ProductId,
            Quantity.Create(3).Value, InfrastructureTestData.Now);

        (await repository.SaveAsync(first, 1, default)).Value.Should().Be(2);
        var conflict = await repository.SaveAsync(stale, 1, default);

        conflict.Error.Code.Should().Be("order.version_conflict");
        stale.DomainEvents.Should().NotBeEmpty();
        var persisted = await repository.LoadAsync(order.Id, default);
        persisted.Value.Order.Items.Single().Quantity.Value.Should().Be(2);
        persisted.Value.Version.Should().Be(2);
        first.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task AggregateAndNestedStateAreIsolatedAcrossLoads()
    {
        var repository = new InMemoryOrderRepository();
        var order = InfrastructureTestData.Order();
        await repository.AddAsync(order, default);
        order.ChangeItemQuantity(order.Items.Single().ProductId,
            Quantity.Create(4).Value, InfrastructureTestData.Now);

        var first = (await repository.LoadAsync(order.Id, default)).Value.Order;
        first.ChangeItemQuantity(first.Items.Single().ProductId,
            Quantity.Create(2).Value, InfrastructureTestData.Now);
        var second = (await repository.LoadAsync(order.Id, default)).Value.Order;

        second.Should().NotBeSameAs(first);
        second.Items.Single().Should().NotBeSameAs(first.Items.Single());
        second.Items.Single().Quantity.Value.Should().Be(1);
        second.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentStaleSavesAllowExactlyOneWinner()
    {
        var repository = new InMemoryOrderRepository();
        var order = InfrastructureTestData.Order();
        await repository.AddAsync(order, default);
        var left = (await repository.LoadAsync(order.Id, default)).Value.Order;
        var right = (await repository.LoadAsync(order.Id, default)).Value.Order;
        left.ChangeItemQuantity(left.Items.Single().ProductId,
            Quantity.Create(2).Value, InfrastructureTestData.Now);
        right.ChangeItemQuantity(right.Items.Single().ProductId,
            Quantity.Create(3).Value, InfrastructureTestData.Now);

        var results = await Task.WhenAll(
            Task.Run(() => repository.SaveAsync(left, 1, default)),
            Task.Run(() => repository.SaveAsync(right, 1, default)));

        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Code
            .Should().Be("order.version_conflict");
        var persisted = await repository.LoadAsync(order.Id, default);
        persisted.Value.Version.Should().Be(2);
        persisted.Value.Order.Items.Single().Quantity.Value
            .Should().BeOneOf(2, 3);
    }

    [Fact]
    public async Task ConcurrentDuplicateAddsAllowExactlyOneWinner()
    {
        var repository = new InMemoryOrderRepository();
        var id = OrderId.New();
        var results = await Task.WhenAll(
            Task.Run(() => repository.AddAsync(
                InfrastructureTestData.Order(id), default)),
            Task.Run(() => repository.AddAsync(
                InfrastructureTestData.Order(id), default)));

        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Code
            .Should().Be("order.already_exists");
    }

    [Fact]
    public async Task CheckoutCancellationAndExpirationStateSurviveRehydration()
    {
        var repository = new InMemoryOrderRepository();
        var cancelled = InfrastructureTestData.Order();
        await repository.AddAsync(cancelled, default);
        InfrastructureTestData.MakeAwaitingPayment(cancelled);
        cancelled.Cancel(
            CancellationReason.Create("customer request").Value,
            InfrastructureTestData.Now.AddMinutes(4));
        await repository.SaveAsync(cancelled, 1, default);

        var loadedCancelled =
            (await repository.LoadAsync(cancelled.Id, default)).Value.Order;
        loadedCancelled.Status.Should().Be(OrderStatus.Cancelled);
        loadedCancelled.Cancellation.Should().NotBeNull();
        loadedCancelled.CheckoutAttempt!.FulfillmentPlan.Should().NotBeNull();
        loadedCancelled.CheckoutAttempt.Reservations.Should().ContainSingle();
        loadedCancelled.CheckoutAttempt.Reservations.Single()
            .Should().NotBeSameAs(
                cancelled.CheckoutAttempt!.Reservations.Single());

        var expired = InfrastructureTestData.Order();
        await repository.AddAsync(expired, default);
        InfrastructureTestData.MakeAwaitingPayment(expired);
        expired.Expire(expired.PaymentExpiresAt!.Value);
        await repository.SaveAsync(expired, 1, default);

        var loadedExpired =
            (await repository.LoadAsync(expired.Id, default)).Value.Order;
        loadedExpired.Status.Should().Be(OrderStatus.Expired);
        loadedExpired.ExpiredAt.Should().Be(expired.ExpiredAt);
    }
}

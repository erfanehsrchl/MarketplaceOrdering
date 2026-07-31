using FluentAssertions;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Infrastructure.Events;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Events;

/// <summary>
/// Domain Events must survive the commit boundary. Clearing them on a
/// successful save is only safe because they are recorded first.
/// </summary>
public sealed class InMemoryDomainEventOutboxTests
{
    [Fact]
    public async Task SuccessfulPersistence_ShouldRecordEventsAndClearThem()
    {
        var outbox = new InMemoryDomainEventOutbox();
        var repository = new InMemoryOrderRepository(outbox);
        var order = ApplicationTestData.CreateOrder();
        var pendingCount = order.DomainEvents.Count;

        await repository.AddAsync(order, CancellationToken.None);

        order.DomainEvents.Should().BeEmpty();
        var entries = outbox.Read();
        entries.Should().HaveCount(pendingCount);
        entries.Should().OnlyContain(entry =>
            entry.OrderId == order.Id && entry.Version == 1);
        entries.Select(entry => entry.Sequence).Should()
            .BeInAscendingOrder().And.OnlyHaveUniqueItems();
        entries.First().DomainEvent.Should().BeOfType<OrderCreatedDomainEvent>();
    }

    /// <summary>
    /// A failed write must leave the Aggregate exactly as it was, events
    /// included, so the caller can retry without losing or duplicating facts.
    /// </summary>
    [Fact]
    public async Task FailedPersistence_ShouldRecordNothingAndKeepEventsPending()
    {
        var outbox = new InMemoryDomainEventOutbox();
        var repository = new InMemoryOrderRepository(outbox);
        var order = ApplicationTestData.CreateOrder();

        var save = await repository.SaveAsync(order, CancellationToken.None);

        save.IsFailure.Should().BeTrue();
        outbox.Read().Should().BeEmpty();
        order.DomainEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EveryCommit_ShouldBeTaggedWithTheVersionItProduced()
    {
        var outbox = new InMemoryDomainEventOutbox();
        var repository = new InMemoryOrderRepository(outbox);
        var order = ApplicationTestData.CreateOrder();
        await repository.AddAsync(order, CancellationToken.None);

        order.AddItem(
            ApplicationTestData.Product(2),
            MarketplaceOrdering.Domain.ValueObjects.Quantity.Create(1).Value,
            order.CreatedAt);
        await repository.SaveAsync(order, CancellationToken.None);

        outbox.Read(order.Id).Should().Contain(entry =>
            entry.Version == 2
            && entry.DomainEvent is OrderItemAddedDomainEvent);
    }

    [Fact]
    public async Task Read_ShouldFilterByOrderAndResetShouldClear()
    {
        var outbox = new InMemoryDomainEventOutbox();
        var repository = new InMemoryOrderRepository(outbox);
        var first = ApplicationTestData.CreateOrder();
        var second = ApplicationTestData.CreateOrder();
        await repository.AddAsync(first, CancellationToken.None);
        await repository.AddAsync(second, CancellationToken.None);

        outbox.Read(first.Id).Should().OnlyContain(
            entry => entry.OrderId == first.Id);
        outbox.Read().Count.Should().BeGreaterThan(
            outbox.Read(first.Id).Count);

        outbox.Reset();
        outbox.Read().Should().BeEmpty();
    }
}

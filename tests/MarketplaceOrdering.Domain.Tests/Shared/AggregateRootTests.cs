using FluentAssertions;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Tests.Shared;

public sealed class AggregateRootTests
{
    [Fact]
    public void RaisedEvents_ShouldPreserveOrderAndReadingShouldNotClearThem()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        var first = new TestEvent(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "first");
        var second = new TestEvent(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "second");

        aggregate.Raise(first);
        aggregate.Raise(second);

        aggregate.DomainEvents.Should().ContainInOrder(first, second);
        aggregate.DomainEvents.Should().HaveCount(2);
    }

    [Fact]
    public void ExposedEvents_ShouldNotAllowMutation()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.Raise(new TestEvent(DateTimeOffset.UnixEpoch, "event"));

        var mutation = () => ((ICollection<IDomainEvent>)aggregate.DomainEvents).Clear();

        mutation.Should().Throw<NotSupportedException>();
        aggregate.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void CommittedEvents_ShouldOnlyClearThroughInternalOperation()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.Raise(new TestEvent(DateTimeOffset.UnixEpoch, "event"));

        aggregate.ClearCommittedDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void VersionUpdate_ShouldAcceptNonNegativeVersionAndRejectNegativeVersion()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.UpdateVersion(7);

        aggregate.Version.Should().Be(7);
        var update = () => aggregate.UpdateVersion(-1);
        update.Should().Throw<ArgumentOutOfRangeException>();
        typeof(AggregateRoot<Guid>).GetProperty(nameof(AggregateRoot<Guid>.Version))!
            .SetMethod!.IsPublic.Should().BeFalse();
    }

    [Fact]
    public void Event_ShouldUseProvidedTimeAndGenerateNonEmptyIdentifier()
    {
        var occurredAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var domainEvent = new TestEvent(occurredAt, "event");

        domainEvent.OccurredAt.Should().Be(occurredAt);
        domainEvent.EventId.Should().NotBeEmpty();
    }

    private sealed class TestAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }

    private sealed record TestEvent(DateTimeOffset Time, string Name) : DomainEvent(Time);
}

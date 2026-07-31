using System.Collections.ObjectModel;

namespace MarketplaceOrdering.Domain.Shared;

public abstract class AggregateRoot<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly ReadOnlyCollection<IDomainEvent> _readOnlyDomainEvents;

    protected AggregateRoot(TId id)
    {
        Id = id;
        _readOnlyDomainEvents = _domainEvents.AsReadOnly();
    }

    public TId Id { get; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _readOnlyDomainEvents;

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    internal void ClearCommittedDomainEvents() => _domainEvents.Clear();

}

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

    public long Version { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _readOnlyDomainEvents;

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    internal void ClearCommittedDomainEvents() => _domainEvents.Clear();

    internal void UpdateVersion(long version)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version cannot be negative.");
        }

        Version = version;
    }
}

using MarketplaceOrdering.Domain.Orders;

namespace MarketplaceOrdering.Application.Common.Models;

public sealed record VersionedOrder
{
    public VersionedOrder(Order order, long version)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        Order = order;
        Version = version;
    }

    public Order Order { get; }
    public long Version { get; }
}

using System.Collections.ObjectModel;

namespace MarketplaceOrdering.Domain.Fulfillment;

internal sealed class ProductAllocationOption
{
    private readonly ReadOnlyCollection<ProductAllocation> _allocations;

    internal ProductAllocationOption(
        ProductDemand demand,
        IEnumerable<ProductAllocation> allocations)
    {
        Demand = demand;
        _allocations = Array.AsReadOnly(allocations
            .OrderBy(allocation => allocation.VendorId.Value)
            .ToArray());
    }

    internal ProductDemand Demand { get; }
    internal IReadOnlyCollection<ProductAllocation> Allocations => _allocations;
}

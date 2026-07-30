using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders;

public readonly record struct InitialOrderItem(
    ProductReference Product,
    Quantity Quantity);

using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Fulfillment;

public readonly record struct ProductDemand(
    ProductReference Product,
    Quantity Quantity);

using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders;

public readonly record struct ProductReference(
    ProductId ProductId,
    ProductName ProductName);

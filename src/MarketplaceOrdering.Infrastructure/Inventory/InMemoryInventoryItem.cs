using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Infrastructure.Inventory;

public sealed record InMemoryInventoryItem(
    VendorId VendorId,
    ProductId ProductId,
    int AvailableQuantity);

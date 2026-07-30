using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders;

public readonly record struct SelectedDiscountCode(
    DiscountCode Code,
    DateTimeOffset SelectedAt);

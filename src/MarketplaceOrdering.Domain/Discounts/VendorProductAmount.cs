using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Discounts;

public readonly record struct VendorProductAmount(
    VendorId VendorId,
    MoneyValue ProductsAmount);

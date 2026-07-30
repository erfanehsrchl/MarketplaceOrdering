using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Discounts;

public readonly record struct VendorDiscountAllocation(
    VendorId VendorId,
    MoneyValue DiscountAmount);

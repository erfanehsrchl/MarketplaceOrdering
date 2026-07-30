using System.Collections.ObjectModel;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Shared;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Fulfillment;

internal sealed class FulfillmentCandidate
{
    private readonly ReadOnlyCollection<ProductAllocation> _allocations;
    private readonly ReadOnlyCollection<VendorFulfillment> _vendors;

    internal FulfillmentCandidate(
        IEnumerable<ProductAllocation> allocations,
        IEnumerable<VendorFulfillment> vendors,
        MoneyValue productsAmount, MoneyValue discountAmount,
        MoneyValue shippingAmount, MoneyValue totalPayable,
        DiscountCalculation? discountCalculation,
        int maximumDeliveryHours, Error? discountError = null)
    {
        _allocations = Array.AsReadOnly(allocations
            .OrderBy(allocation => allocation.VendorId.Value)
            .ThenBy(allocation => allocation.ProductId.Value)
            .ThenBy(allocation => allocation.Quantity.Value)
            .ThenBy(allocation => allocation.UnitPrice.Amount).ToArray());
        _vendors = Array.AsReadOnly(vendors
            .OrderBy(vendor => vendor.VendorId.Value).ToArray());
        ProductsAmount = productsAmount;
        DiscountAmount = discountAmount;
        ShippingAmount = shippingAmount;
        TotalPayable = totalPayable;
        DiscountCalculation = discountCalculation;
        MaximumDeliveryHours = maximumDeliveryHours;
        DiscountError = discountError;
    }

    internal IReadOnlyCollection<ProductAllocation> Allocations => _allocations;
    internal IReadOnlyCollection<VendorFulfillment> Vendors => _vendors;
    internal MoneyValue ProductsAmount { get; }
    internal MoneyValue DiscountAmount { get; }
    internal MoneyValue ShippingAmount { get; }
    internal MoneyValue TotalPayable { get; }
    internal DiscountCalculation? DiscountCalculation { get; }
    internal int MaximumDeliveryHours { get; }
    internal Error? DiscountError { get; }
}

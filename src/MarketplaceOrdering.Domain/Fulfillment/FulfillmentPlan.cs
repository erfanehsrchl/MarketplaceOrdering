using System.Collections.ObjectModel;
using MarketplaceOrdering.Domain.Discounts;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Fulfillment;

public sealed class FulfillmentPlan
{
    private readonly ReadOnlyCollection<VendorFulfillment> _vendors;

    internal FulfillmentPlan(FulfillmentCandidate candidate)
    {
        _vendors = Array.AsReadOnly(candidate.Vendors.ToArray());
        ProductsAmount = candidate.ProductsAmount;
        DiscountAmount = candidate.DiscountAmount;
        ShippingAmount = candidate.ShippingAmount;
        TotalPayable = candidate.TotalPayable;
        DiscountCalculation = candidate.DiscountCalculation;
        MaximumDeliveryHours = candidate.MaximumDeliveryHours;
    }

    public IReadOnlyCollection<VendorFulfillment> Vendors => _vendors;
    public MoneyValue ProductsAmount { get; }
    public MoneyValue DiscountAmount { get; }
    public MoneyValue ShippingAmount { get; }
    public MoneyValue TotalPayable { get; }
    public DiscountCalculation? DiscountCalculation { get; }
    public int MaximumDeliveryHours { get; }
    public int VendorCount => _vendors.Count;
    public IReadOnlyCollection<ProductAllocation> ProductAllocations =>
        _vendors.SelectMany(vendor => vendor.ProductAllocations)
            .OrderBy(allocation => allocation.VendorId.Value)
            .ThenBy(allocation => allocation.ProductId.Value)
            .ToArray();
}

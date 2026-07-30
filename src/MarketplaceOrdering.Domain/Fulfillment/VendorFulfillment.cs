using System.Collections.ObjectModel;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Fulfillment;

public sealed class VendorFulfillment
{
    private readonly ReadOnlyCollection<ProductAllocation> _productAllocations;

    private VendorFulfillment(
        VendorId vendorId, IReadOnlyCollection<ProductAllocation> allocations,
        MoneyValue productsAmount, MoneyValue discountAmount,
        MoneyValue shippingCost, MoneyValue totalPayable,
        MoneyValue minimumOrderAmount, int estimatedDeliveryHours)
    {
        VendorId = vendorId;
        _productAllocations = Array.AsReadOnly(allocations
            .OrderBy(allocation => allocation.ProductId.Value).ToArray());
        ProductsAmount = productsAmount;
        DiscountAmount = discountAmount;
        ShippingCost = shippingCost;
        TotalPayable = totalPayable;
        MinimumOrderAmount = minimumOrderAmount;
        EstimatedDeliveryHours = estimatedDeliveryHours;
    }

    public VendorId VendorId { get; }
    public IReadOnlyCollection<ProductAllocation> ProductAllocations =>
        _productAllocations;
    public MoneyValue ProductsAmount { get; }
    public MoneyValue DiscountAmount { get; }
    public MoneyValue ShippingCost { get; }
    public MoneyValue TotalPayable { get; }
    public MoneyValue MinimumOrderAmount { get; }
    public int EstimatedDeliveryHours { get; }

    internal static Result<VendorFulfillment> Create(
        VendorId vendorId, IReadOnlyCollection<ProductAllocation> allocations,
        MoneyValue productsAmount, MoneyValue discountAmount,
        MoneyValue shippingCost, MoneyValue minimumOrderAmount)
    {
        if (productsAmount.Amount < minimumOrderAmount.Amount
            || discountAmount.Amount > productsAmount.Amount
            || allocations.Count == 0)
        {
            return Result<VendorFulfillment>.Failure(
                FulfillmentErrors.InvalidAllocation);
        }

        var afterDiscount = productsAmount.Subtract(discountAmount);
        if (afterDiscount.IsFailure)
        {
            return Result<VendorFulfillment>.Failure(
                FulfillmentErrors.InvalidAllocation);
        }

        var payable = afterDiscount.Value.Add(shippingCost);
        if (payable.IsFailure)
        {
            return Result<VendorFulfillment>.Failure(
                FulfillmentErrors.CalculationOverflow);
        }

        return Result<VendorFulfillment>.Success(new VendorFulfillment(
            vendorId, allocations, productsAmount, discountAmount,
            shippingCost, payable.Value, minimumOrderAmount,
            allocations.Max(allocation => allocation.EstimatedDeliveryHours)));
    }
}

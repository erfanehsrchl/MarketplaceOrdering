using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Fulfillment;

public sealed class ProductAllocation
{
    private ProductAllocation(
        VendorId vendorId, ProductId productId, ProductName productName,
        Quantity quantity, MoneyValue unitPrice, MoneyValue lineTotal,
        int estimatedDeliveryHours)
    {
        VendorId = vendorId;
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
        EstimatedDeliveryHours = estimatedDeliveryHours;
    }

    public VendorId VendorId { get; }
    public ProductId ProductId { get; }
    public ProductName ProductName { get; }
    public Quantity Quantity { get; }
    public MoneyValue UnitPrice { get; }
    public MoneyValue LineTotal { get; }
    public int EstimatedDeliveryHours { get; }

    public static Result<ProductAllocation> Create(
        VendorId vendorId, ProductId productId, ProductName productName,
        Quantity quantity, MoneyValue unitPrice, int estimatedDeliveryHours)
    {
        if (quantity.Value <= 0 || unitPrice.Amount == 0
            || estimatedDeliveryHours <= 0)
        {
            return Result<ProductAllocation>.Failure(
                FulfillmentErrors.InvalidAllocation);
        }

        var total = unitPrice.Multiply(quantity.Value);
        return total.IsFailure
            ? Result<ProductAllocation>.Failure(FulfillmentErrors.CalculationOverflow)
            : Result<ProductAllocation>.Success(new ProductAllocation(
                vendorId, productId, productName, quantity, unitPrice,
                total.Value, estimatedDeliveryHours));
    }
}

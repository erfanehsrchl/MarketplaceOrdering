using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Fulfillment;

public sealed record ProductOffer
{
    private ProductOffer(
        VendorId vendorId, ProductId productId, MoneyValue unitPrice,
        int availableQuantity, MoneyValue shippingCost,
        MoneyValue minimumOrderAmount, int estimatedDeliveryHours)
    {
        VendorId = vendorId;
        ProductId = productId;
        UnitPrice = unitPrice;
        AvailableQuantity = availableQuantity;
        ShippingCost = shippingCost;
        MinimumOrderAmount = minimumOrderAmount;
        EstimatedDeliveryHours = estimatedDeliveryHours;
    }

    public VendorId VendorId { get; }
    public ProductId ProductId { get; }
    public MoneyValue UnitPrice { get; }
    public int AvailableQuantity { get; }
    public MoneyValue ShippingCost { get; }
    public MoneyValue MinimumOrderAmount { get; }
    public int EstimatedDeliveryHours { get; }

    public static Result<ProductOffer> Create(
        VendorId vendorId, ProductId productId, MoneyValue unitPrice,
        int availableQuantity, MoneyValue shippingCost,
        MoneyValue minimumOrderAmount, int estimatedDeliveryHours) =>
        estimatedDeliveryHours <= 0
            ? Result<ProductOffer>.Failure(FulfillmentErrors.InvalidDeliveryHours)
            : Result<ProductOffer>.Success(new ProductOffer(
                vendorId, productId, unitPrice, availableQuantity,
                shippingCost, minimumOrderAmount, estimatedDeliveryHours));
}

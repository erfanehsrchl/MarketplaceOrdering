using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders;

public sealed class OrderItem
{
    public const int MaximumQuantityPerProduct = 10;

    private OrderItem(ProductReference product, Quantity quantity)
    {
        ProductId = product.ProductId;
        ProductName = product.ProductName;
        Quantity = quantity;
    }

    public ProductId ProductId { get; }

    public ProductName ProductName { get; }

    public Quantity Quantity { get; private set; }

    internal static Result<OrderItem> Create(ProductReference product, Quantity quantity)
    {
        if (quantity.Value <= 0)
        {
            return Result<OrderItem>.Failure(QuantityErrors.NotPositive);
        }

        return quantity.Value > MaximumQuantityPerProduct
            ? Result<OrderItem>.Failure(
                OrderErrors.QuantityLimitExceeded(product.ProductId, quantity.Value))
            : Result<OrderItem>.Success(new OrderItem(product, quantity));
    }

    internal static OrderItem Rehydrate(
        ProductId productId,
        ProductName productName,
        Quantity quantity) =>
        new(new ProductReference(productId, productName), quantity);

    internal Result IncreaseQuantity(Quantity addedQuantity)
    {
        if (addedQuantity.Value <= 0)
        {
            return Result.Failure(QuantityErrors.NotPositive);
        }

        var requestedQuantity = (long)Quantity.Value + addedQuantity.Value;
        if (requestedQuantity > MaximumQuantityPerProduct)
        {
            return Result.Failure(
                OrderErrors.QuantityLimitExceeded(ProductId, (int)requestedQuantity));
        }

        Quantity = MarketplaceOrdering.Domain.ValueObjects.Quantity
            .Create((int)requestedQuantity)
            .Value;
        return Result.Success();
    }

    internal Result ChangeQuantity(Quantity newQuantity)
    {
        if (newQuantity.Value <= 0)
        {
            return Result.Failure(QuantityErrors.NotPositive);
        }

        if (newQuantity.Value > MaximumQuantityPerProduct)
        {
            return Result.Failure(
                OrderErrors.QuantityLimitExceeded(ProductId, newQuantity.Value));
        }

        Quantity = newQuantity;
        return Result.Success();
    }
}

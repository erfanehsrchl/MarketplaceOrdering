using System.Globalization;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders;

public static class OrderErrors
{
    public static Error ItemsRequired { get; } =
        Error.Validation("order.items_required", "An order requires at least one item.");

    public static Error NotEditable { get; } =
        Error.BusinessRule("order.not_editable", "Only a draft order can be edited.");

    public static Error LastItemCannotBeRemoved { get; } =
        Error.BusinessRule(
            "order.last_item_cannot_be_removed",
            "The final order item cannot be removed.");

    public static Error ProductNotFound(ProductId productId) =>
        Error.NotFound(
            "order.product_not_found",
            "The product was not found in the order.",
            new Dictionary<string, string>
            {
                ["productId"] = productId.ToString()
            });

    public static Error QuantityLimitExceeded(ProductId productId, int requestedQuantity) =>
        Error.BusinessRule(
            "order.quantity_limit_exceeded",
            $"Quantity cannot exceed {OrderItem.MaximumQuantityPerProduct} for one product.",
            new Dictionary<string, string>
            {
                ["productId"] = productId.ToString(),
                ["requestedQuantity"] = requestedQuantity.ToString(CultureInfo.InvariantCulture),
                ["maximumQuantity"] =
                    OrderItem.MaximumQuantityPerProduct.ToString(CultureInfo.InvariantCulture)
            });
}

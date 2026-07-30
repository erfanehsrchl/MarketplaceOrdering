using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Orders;

namespace MarketplaceOrdering.Application.Orders.Mapping;

public static class OrderDetailsMapper
{
    public static OrderDetails Map(Order order, long version)
    {
        ArgumentNullException.ThrowIfNull(order);
        var selectedDiscount = order.SelectedDiscount is { } selected
            ? new SelectedDiscountDetails(
                selected.Code.Value,
                selected.SelectedAt)
            : null;
        var attempt = order.CheckoutAttempt;
        var checkout = attempt is null
            ? null
            : new CheckoutAttemptSummary(
                attempt.Id.Value,
                attempt.Status.ToString(),
                attempt.StartedAt,
                attempt.CompletedAt,
                attempt.FulfillmentPlan?.TotalPayable.Amount,
                attempt.PaymentExpiresAt);

        return new OrderDetails(
            order.Id.Value,
            order.CustomerId.Value,
            order.DeliveryAddress.Value,
            order.Status.ToString(),
            order.CreatedAt,
            version,
            order.Items.Select(item => new OrderItemDetails(
                item.ProductId.Value,
                item.ProductName.Value,
                item.Quantity.Value)).ToArray(),
            selectedDiscount,
            checkout);
    }
}

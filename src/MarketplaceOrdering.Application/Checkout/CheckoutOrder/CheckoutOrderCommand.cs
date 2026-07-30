namespace MarketplaceOrdering.Application.Checkout.CheckoutOrder;

public sealed record CheckoutOrderCommand(
    Guid OrderId,
    string IdempotencyKey);

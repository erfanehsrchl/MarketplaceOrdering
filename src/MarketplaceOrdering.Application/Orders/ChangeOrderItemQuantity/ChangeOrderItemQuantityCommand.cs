namespace MarketplaceOrdering.Application.Orders.ChangeOrderItemQuantity;

public sealed record ChangeOrderItemQuantityCommand(
    Guid OrderId,
    Guid ProductId,
    int Quantity);

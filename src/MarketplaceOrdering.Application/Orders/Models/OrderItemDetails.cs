namespace MarketplaceOrdering.Application.Orders.Models;

public sealed record OrderItemDetails(
    Guid ProductId,
    string ProductName,
    int Quantity);

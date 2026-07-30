namespace MarketplaceOrdering.Application.Orders.ApplyDiscountCode;

public sealed record ApplyDiscountCodeCommand(
    Guid OrderId,
    string DiscountCode);

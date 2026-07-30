namespace MarketplaceOrdering.Api.Contracts.Orders;

public sealed record CreateOrderRequest(
    Guid CustomerId,
    string DeliveryAddress,
    IReadOnlyCollection<CreateOrderItemRequest>? Items);

public sealed record CreateOrderItemRequest(
    Guid ProductId,
    string ProductName,
    int Quantity);

public sealed record AddOrderItemRequest(
    Guid ProductId,
    string ProductName,
    int Quantity);

public sealed record ChangeOrderItemQuantityRequest(int Quantity);

public sealed record ApplyDiscountCodeRequest(string DiscountCode);

public sealed record CancelOrderRequest(string Reason);

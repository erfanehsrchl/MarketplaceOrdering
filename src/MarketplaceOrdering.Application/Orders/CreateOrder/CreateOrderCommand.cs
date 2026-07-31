using MediatR;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.CreateOrder;

public sealed record CreateOrderCommand(
    Guid CustomerId,
    string DeliveryAddress,
    IReadOnlyList<CreateOrderItemInput>? Items)
    : IRequest<Result<OrderDetails>>;

public sealed record CreateOrderItemInput(
    Guid ProductId,
    string ProductName,
    int Quantity);

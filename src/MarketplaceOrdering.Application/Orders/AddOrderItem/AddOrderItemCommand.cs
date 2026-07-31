using MediatR;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.AddOrderItem;

public sealed record AddOrderItemCommand(
    Guid OrderId,
    Guid ProductId,
    string ProductName,
    int Quantity) : IRequest<Result<OrderDetails>>;

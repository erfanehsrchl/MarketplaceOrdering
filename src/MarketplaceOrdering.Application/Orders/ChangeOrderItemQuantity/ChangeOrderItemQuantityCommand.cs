using MediatR;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.ChangeOrderItemQuantity;

public sealed record ChangeOrderItemQuantityCommand(
    Guid OrderId,
    Guid ProductId,
    int Quantity) : IRequest<Result<OrderDetails>>;

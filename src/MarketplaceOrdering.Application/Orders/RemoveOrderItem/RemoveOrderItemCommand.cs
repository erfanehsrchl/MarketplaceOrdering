using MediatR;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.RemoveOrderItem;

public sealed record RemoveOrderItemCommand(
    Guid OrderId,
    Guid ProductId) : IRequest<Result<OrderDetails>>;

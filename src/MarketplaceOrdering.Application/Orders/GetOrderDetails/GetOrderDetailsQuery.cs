using MediatR;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.GetOrderDetails;

public sealed record GetOrderDetailsQuery(Guid OrderId)
    : IRequest<Result<OrderDetails>>;

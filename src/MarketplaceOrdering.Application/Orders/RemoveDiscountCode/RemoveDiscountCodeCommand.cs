using MediatR;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.RemoveDiscountCode;

public sealed record RemoveDiscountCodeCommand(Guid OrderId)
    : IRequest<Result<OrderDetails>>;

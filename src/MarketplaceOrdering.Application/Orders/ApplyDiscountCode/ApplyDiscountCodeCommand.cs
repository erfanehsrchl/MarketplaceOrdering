using MediatR;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.ApplyDiscountCode;

public sealed record ApplyDiscountCodeCommand(
    Guid OrderId,
    string DiscountCode) : IRequest<Result<OrderDetails>>;

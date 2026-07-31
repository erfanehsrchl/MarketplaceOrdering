using MediatR;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Checkout.CheckoutOrder;

public sealed record CheckoutOrderCommand(
    Guid OrderId,
    string IdempotencyKey) : IRequest<Result<CheckoutOperationResult>>;

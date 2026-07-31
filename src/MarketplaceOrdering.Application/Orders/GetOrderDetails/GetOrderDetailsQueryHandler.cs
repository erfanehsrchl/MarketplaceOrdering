using MediatR;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.Mapping;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Orders.GetOrderDetails;

public sealed class GetOrderDetailsQueryHandler
    : IRequestHandler<GetOrderDetailsQuery, Result<OrderDetails>>
{
    private readonly IOrderRepository _repository;

    public GetOrderDetailsQueryHandler(IOrderRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<OrderDetails>> Handle(
        GetOrderDetailsQuery query,
        CancellationToken cancellationToken)
    {
        if (query is null)
            return Result<OrderDetails>.Failure(ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(query.OrderId);
        if (orderId.IsFailure) return Result<OrderDetails>.Failure(orderId.Error);
        var loaded = await _repository.LoadAsync(orderId.Value, cancellationToken);
        return loaded.IsFailure
            ? Result<OrderDetails>.Failure(loaded.Error)
            : Result<OrderDetails>.Success(
                OrderDetailsMapper.Map(loaded.Value.Order, loaded.Value.Version));
    }
}

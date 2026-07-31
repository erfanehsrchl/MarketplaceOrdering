using MediatR;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.Mapping;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Orders.AddOrderItem;

public sealed class AddOrderItemCommandHandler
    : IRequestHandler<AddOrderItemCommand, Result<OrderDetails>>
{
    private readonly IOrderRepository _repository;
    private readonly IClock _clock;

    public AddOrderItemCommandHandler(IOrderRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<OrderDetails>> Handle(
        AddOrderItemCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            return Result<OrderDetails>.Failure(ApplicationErrors.InvalidRequest);
        var orderId = OrderId.Create(command.OrderId);
        if (orderId.IsFailure) return Result<OrderDetails>.Failure(orderId.Error);
        var productId = ProductId.Create(command.ProductId);
        if (productId.IsFailure) return Result<OrderDetails>.Failure(productId.Error);
        var productName = ProductName.Create(command.ProductName);
        if (productName.IsFailure) return Result<OrderDetails>.Failure(productName.Error);
        var quantity = Quantity.Create(command.Quantity);
        if (quantity.IsFailure) return Result<OrderDetails>.Failure(quantity.Error);

        var loaded = await _repository.LoadAsync(orderId.Value, cancellationToken);
        if (loaded.IsFailure) return Result<OrderDetails>.Failure(loaded.Error);
        var order = loaded.Value;
        var changed = order.AddItem(
            new ProductReference(productId.Value, productName.Value),
            quantity.Value,
            _clock.UtcNow);
        if (changed.IsFailure) return Result<OrderDetails>.Failure(changed.Error);
        var saved = await _repository.SaveAsync(order, cancellationToken);
        return saved.IsFailure
            ? Result<OrderDetails>.Failure(saved.Error)
            : Result<OrderDetails>.Success(
                OrderDetailsMapper.Map(order));
    }
}

using MediatR;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.Mapping;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Orders.CreateOrder;

public sealed class CreateOrderCommandHandler
    : IRequestHandler<CreateOrderCommand, Result<OrderDetails>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IClock _clock;

    public CreateOrderCommandHandler(IOrderRepository orderRepository, IClock clock)
    {
        _orderRepository = orderRepository;
        _clock = clock;
    }

    public async Task<Result<OrderDetails>> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null || command.Items is null)
            return Result<OrderDetails>.Failure(ApplicationErrors.InvalidRequest);

        var customerId = CustomerId.Create(command.CustomerId);
        if (customerId.IsFailure)
            return Result<OrderDetails>.Failure(customerId.Error);
        var address = DeliveryAddress.Create(command.DeliveryAddress);
        if (address.IsFailure)
            return Result<OrderDetails>.Failure(address.Error);

        var items = new List<InitialOrderItem>();
        foreach (var input in command.Items)
        {
            if (input is null)
                return Result<OrderDetails>.Failure(ApplicationErrors.InvalidRequest);
            var productId = ProductId.Create(input.ProductId);
            if (productId.IsFailure)
                return Result<OrderDetails>.Failure(productId.Error);
            var productName = ProductName.Create(input.ProductName);
            if (productName.IsFailure)
                return Result<OrderDetails>.Failure(productName.Error);
            var quantity = Quantity.Create(input.Quantity);
            if (quantity.IsFailure)
                return Result<OrderDetails>.Failure(quantity.Error);
            items.Add(new InitialOrderItem(
                new ProductReference(productId.Value, productName.Value),
                quantity.Value));
        }

        var createdAt = _clock.UtcNow;
        var order = Order.Create(
            OrderId.New(),
            customerId.Value,
            address.Value,
            items,
            createdAt);
        if (order.IsFailure)
            return Result<OrderDetails>.Failure(order.Error);

        var added = await _orderRepository.AddAsync(
            order.Value, cancellationToken);
        return added.IsFailure
            ? Result<OrderDetails>.Failure(added.Error)
            : Result<OrderDetails>.Success(
                OrderDetailsMapper.Map(order.Value));
    }
}

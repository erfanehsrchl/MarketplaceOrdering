using System.Collections.ObjectModel;
using MediatR;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Orders.CreateOrder;

public sealed record CreateOrderCommand : IRequest<Result<OrderDetails>>
{
    private readonly ReadOnlyCollection<CreateOrderItemInput>? _items;

    public CreateOrderCommand(
        Guid customerId,
        string deliveryAddress,
        IReadOnlyCollection<CreateOrderItemInput>? items)
    {
        CustomerId = customerId;
        DeliveryAddress = deliveryAddress;
        _items = items is null ? null : Array.AsReadOnly(items.ToArray());
    }

    public Guid CustomerId { get; }
    public string DeliveryAddress { get; }
    public IReadOnlyCollection<CreateOrderItemInput>? Items => _items;
}

public sealed record CreateOrderItemInput(
    Guid ProductId,
    string ProductName,
    int Quantity);

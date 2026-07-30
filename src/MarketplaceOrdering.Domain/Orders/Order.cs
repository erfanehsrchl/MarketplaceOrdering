using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders;

public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];

    private Order(
        OrderId orderId,
        CustomerId customerId,
        DeliveryAddress deliveryAddress,
        IEnumerable<OrderItem> items,
        DateTimeOffset createdAt)
        : base(orderId)
    {
        CustomerId = customerId;
        DeliveryAddress = deliveryAddress;
        _items.AddRange(items);
        CreatedAt = createdAt;
        Status = OrderStatus.Draft;
    }

    public CustomerId CustomerId { get; }

    public DeliveryAddress DeliveryAddress { get; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyCollection<OrderItem> Items => _items.ToArray();

    public SelectedDiscountCode? SelectedDiscount { get; private set; }

    public static Result<Order> Create(
        OrderId orderId,
        CustomerId customerId,
        DeliveryAddress deliveryAddress,
        IReadOnlyCollection<InitialOrderItem>? initialItems,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(deliveryAddress);

        var itemsResult = CreateInitialItems(initialItems);
        if (itemsResult.IsFailure)
        {
            return Result<Order>.Failure(itemsResult.Error);
        }

        var order = new Order(
            orderId,
            customerId,
            deliveryAddress,
            itemsResult.Value,
            createdAt);

        order.RaiseDomainEvent(
            new OrderCreatedDomainEvent(orderId, customerId, createdAt));

        foreach (var item in order.Items)
        {
            order.RaiseDomainEvent(
                new OrderItemAddedDomainEvent(
                    orderId,
                    item.ProductId,
                    item.ProductName,
                    item.Quantity,
                    createdAt));
        }

        return Result<Order>.Success(order);
    }

    public Result AddItem(
        ProductReference product,
        Quantity quantity,
        DateTimeOffset occurredAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        var existingItem = FindItem(product.ProductId);
        if (existingItem is null)
        {
            var itemResult = OrderItem.Create(product, quantity);
            if (itemResult.IsFailure)
            {
                return Result.Failure(itemResult.Error);
            }

            _items.Add(itemResult.Value);
            RaiseDomainEvent(
                new OrderItemAddedDomainEvent(
                    Id,
                    itemResult.Value.ProductId,
                    itemResult.Value.ProductName,
                    itemResult.Value.Quantity,
                    occurredAt));

            return Result.Success();
        }

        var previousQuantity = existingItem.Quantity;
        var increaseResult = existingItem.IncreaseQuantity(quantity);
        if (increaseResult.IsFailure)
        {
            return increaseResult;
        }

        RaiseDomainEvent(
            new OrderItemQuantityIncreasedDomainEvent(
                Id,
                existingItem.ProductId,
                previousQuantity,
                quantity,
                existingItem.Quantity,
                occurredAt));

        return Result.Success();
    }

    public Result ChangeItemQuantity(
        ProductId productId,
        Quantity newQuantity,
        DateTimeOffset occurredAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        var item = FindItem(productId);
        if (item is null)
        {
            return Result.Failure(OrderErrors.ProductNotFound(productId));
        }

        var previousQuantity = item.Quantity;
        if (previousQuantity == newQuantity)
        {
            return Result.Success();
        }

        var changeResult = item.ChangeQuantity(newQuantity);
        if (changeResult.IsFailure)
        {
            return changeResult;
        }

        RaiseDomainEvent(
            new OrderItemQuantityChangedDomainEvent(
                Id,
                item.ProductId,
                previousQuantity,
                item.Quantity,
                occurredAt));

        return Result.Success();
    }

    public Result RemoveItem(
        ProductId productId,
        DateTimeOffset occurredAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        var item = FindItem(productId);
        if (item is null)
        {
            return Result.Failure(OrderErrors.ProductNotFound(productId));
        }

        if (_items.Count == 1)
        {
            return Result.Failure(OrderErrors.LastItemCannotBeRemoved);
        }

        _items.Remove(item);
        RaiseDomainEvent(
            new OrderItemRemovedDomainEvent(
                Id,
                item.ProductId,
                item.Quantity,
                occurredAt));

        return Result.Success();
    }

    public Result SelectDiscountCode(
        DiscountCode code,
        DateTimeOffset selectedAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        ArgumentNullException.ThrowIfNull(code);
        if (SelectedDiscount is { } selected && selected.Code == code)
        {
            return Result.Success();
        }

        SelectedDiscount = new SelectedDiscountCode(code, selectedAt);
        RaiseDomainEvent(
            new DiscountCodeSelectedDomainEvent(Id, code, selectedAt));

        return Result.Success();
    }

    public Result RemoveDiscountCode(DateTimeOffset removedAt)
    {
        var draftResult = EnsureDraft();
        if (draftResult.IsFailure)
        {
            return draftResult;
        }

        if (SelectedDiscount is not { } selected)
        {
            return Result.Success();
        }

        SelectedDiscount = null;
        RaiseDomainEvent(
            new DiscountCodeRemovedDomainEvent(Id, selected.Code, removedAt));

        return Result.Success();
    }

    private static Result<List<OrderItem>> CreateInitialItems(
        IReadOnlyCollection<InitialOrderItem>? initialItems)
    {
        if (initialItems is null || initialItems.Count == 0)
        {
            return Result<List<OrderItem>>.Failure(OrderErrors.ItemsRequired);
        }

        var items = new List<OrderItem>();
        foreach (var initialItem in initialItems)
        {
            var existingItem = items.FirstOrDefault(
                item => item.ProductId == initialItem.Product.ProductId);

            if (existingItem is not null)
            {
                var increaseResult = existingItem.IncreaseQuantity(initialItem.Quantity);
                if (increaseResult.IsFailure)
                {
                    return Result<List<OrderItem>>.Failure(increaseResult.Error);
                }

                continue;
            }

            var itemResult = OrderItem.Create(
                initialItem.Product,
                initialItem.Quantity);
            if (itemResult.IsFailure)
            {
                return Result<List<OrderItem>>.Failure(itemResult.Error);
            }

            items.Add(itemResult.Value);
        }

        return Result<List<OrderItem>>.Success(items);
    }

    private OrderItem? FindItem(ProductId productId) =>
        _items.FirstOrDefault(item => item.ProductId == productId);

    private Result EnsureDraft() =>
        Status == OrderStatus.Draft
            ? Result.Success()
            : Result.Failure(OrderErrors.NotEditable);
}

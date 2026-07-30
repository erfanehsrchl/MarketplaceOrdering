using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public readonly record struct OrderId
{
    private OrderId(Guid value) => Value = value;
    public Guid Value { get; }

    public static Result<OrderId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<OrderId>.Failure(Error.Validation("order_id.empty", "Order identifier cannot be empty."))
            : Result<OrderId>.Success(new OrderId(value));

    public static OrderId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

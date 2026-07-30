using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public readonly record struct CustomerId
{
    private CustomerId(Guid value) => Value = value;
    public Guid Value { get; }

    public static Result<CustomerId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<CustomerId>.Failure(Error.Validation("customer_id.empty", "Customer identifier cannot be empty."))
            : Result<CustomerId>.Success(new CustomerId(value));

    public override string ToString() => Value.ToString("D");
}

using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public readonly record struct ProductId
{
    private ProductId(Guid value) => Value = value;
    public Guid Value { get; }

    public static Result<ProductId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<ProductId>.Failure(Error.Validation("product_id.empty", "Product identifier cannot be empty."))
            : Result<ProductId>.Success(new ProductId(value));

    public override string ToString() => Value.ToString("D");
}

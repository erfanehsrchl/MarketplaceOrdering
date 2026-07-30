using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public sealed record ProductName
{
    public const int MaximumLength = 200;

    private ProductName(string value) => Value = value;
    public string Value { get; }

    public static Result<ProductName> Create(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result<ProductName>.Failure(
                Error.Validation("product_name.empty", "Product name cannot be empty."));
        }

        return trimmed.Length > MaximumLength
            ? Result<ProductName>.Failure(Error.Validation("product_name.too_long", $"Product name cannot exceed {MaximumLength} characters."))
            : Result<ProductName>.Success(new ProductName(trimmed));
    }

    public override string ToString() => Value;
}

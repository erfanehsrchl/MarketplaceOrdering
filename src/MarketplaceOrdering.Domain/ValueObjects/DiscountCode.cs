using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public sealed record DiscountCode
{
    public const int MaximumLength = 64;

    private DiscountCode(string value) => Value = value;
    public string Value { get; }

    public static Result<DiscountCode> Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return Result<DiscountCode>.Failure(
                Error.Validation("discount_code.empty", "Discount code cannot be empty."));
        }

        return normalized.Length > MaximumLength
            ? Result<DiscountCode>.Failure(Error.Validation("discount_code.too_long", $"Discount code cannot exceed {MaximumLength} characters."))
            : Result<DiscountCode>.Success(new DiscountCode(normalized));
    }

    public override string ToString() => Value;
}

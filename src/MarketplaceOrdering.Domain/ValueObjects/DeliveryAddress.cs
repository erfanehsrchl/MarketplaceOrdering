using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public sealed record DeliveryAddress
{
    public const int MaximumLength = 1000;

    private DeliveryAddress(string value) => Value = value;
    public string Value { get; }

    public static Result<DeliveryAddress> Create(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result<DeliveryAddress>.Failure(
                Error.Validation("delivery_address.empty", "Delivery address cannot be empty."));
        }

        return trimmed.Length > MaximumLength
            ? Result<DeliveryAddress>.Failure(Error.Validation("delivery_address.too_long", $"Delivery address cannot exceed {MaximumLength} characters."))
            : Result<DeliveryAddress>.Success(new DeliveryAddress(trimmed));
    }

    public override string ToString() => Value;
}

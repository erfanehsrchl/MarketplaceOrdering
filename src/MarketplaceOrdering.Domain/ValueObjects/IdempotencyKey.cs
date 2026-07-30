using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public sealed record IdempotencyKey
{
    public const int MaximumLength = 200;

    private IdempotencyKey(string value) => Value = value;
    public string Value { get; }

    public static Result<IdempotencyKey> Create(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result<IdempotencyKey>.Failure(
                Error.Validation("idempotency_key.empty", "Idempotency key cannot be empty."));
        }

        return trimmed.Length > MaximumLength
            ? Result<IdempotencyKey>.Failure(Error.Validation("idempotency_key.too_long", $"Idempotency key cannot exceed {MaximumLength} characters."))
            : Result<IdempotencyKey>.Success(new IdempotencyKey(trimmed));
    }

    public override string ToString() => Value;
}

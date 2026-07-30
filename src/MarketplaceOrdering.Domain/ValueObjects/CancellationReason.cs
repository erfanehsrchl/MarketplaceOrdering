using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public sealed record CancellationReason
{
    public const int MaximumLength = 500;

    private CancellationReason(string value) => Value = value;
    public string Value { get; }

    public static Result<CancellationReason> Create(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result<CancellationReason>.Failure(
                Error.Validation("cancellation_reason.empty", "Cancellation reason cannot be empty."));
        }

        return trimmed.Length > MaximumLength
            ? Result<CancellationReason>.Failure(Error.Validation("cancellation_reason.too_long", $"Cancellation reason cannot exceed {MaximumLength} characters."))
            : Result<CancellationReason>.Success(new CancellationReason(trimmed));
    }

    public override string ToString() => Value;
}

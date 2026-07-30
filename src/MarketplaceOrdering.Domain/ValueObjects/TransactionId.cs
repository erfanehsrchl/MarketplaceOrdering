using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public sealed record TransactionId
{
    public const int MaximumLength = 128;

    private TransactionId(string value) => Value = value;
    public string Value { get; }

    public static Result<TransactionId> Create(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result<TransactionId>.Failure(
                Error.Validation("transaction_id.empty", "Transaction identifier cannot be empty."));
        }

        return trimmed.Length > MaximumLength
            ? Result<TransactionId>.Failure(Error.Validation("transaction_id.too_long", $"Transaction identifier cannot exceed {MaximumLength} characters."))
            : Result<TransactionId>.Success(new TransactionId(trimmed));
    }

    public override string ToString() => Value;
}

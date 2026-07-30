using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Checkout;

public sealed record CheckoutFailure
{
    public const int MaximumCodeLength = 200;
    private CheckoutFailure(string code, DateTimeOffset occurredAt)
    { Code = code; OccurredAt = occurredAt; }
    public string Code { get; }
    public DateTimeOffset OccurredAt { get; }
    public static Result<CheckoutFailure> Create(string? code, DateTimeOffset occurredAt)
    {
        var normalized = code?.Trim();
        return string.IsNullOrEmpty(normalized) || normalized.Length > MaximumCodeLength
            ? Result<CheckoutFailure>.Failure(CheckoutErrors.FailureCodeRequired)
            : Result<CheckoutFailure>.Success(new CheckoutFailure(normalized, occurredAt));
    }
}

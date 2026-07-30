using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public readonly record struct CheckoutAttemptId
{
    private CheckoutAttemptId(Guid value) => Value = value;
    public Guid Value { get; }

    public static Result<CheckoutAttemptId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<CheckoutAttemptId>.Failure(Error.Validation("checkout_attempt_id.empty", "Checkout attempt identifier cannot be empty."))
            : Result<CheckoutAttemptId>.Success(new CheckoutAttemptId(value));

    public static CheckoutAttemptId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

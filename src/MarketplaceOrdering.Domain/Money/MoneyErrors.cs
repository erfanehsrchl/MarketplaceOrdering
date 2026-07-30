using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Money;

public static class MoneyErrors
{
    public static Error Negative { get; } =
        Error.Validation("money.negative", "Money cannot have a negative amount.");

    public static Error InsufficientAmount { get; } =
        Error.BusinessRule("money.insufficient_amount", "The amount is insufficient for this subtraction.");

    public static Error Overflow { get; } =
        Error.BusinessRule("money.overflow", "The money operation exceeded the supported range.");
}

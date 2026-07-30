using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public static class QuantityErrors
{
    public static Error NotPositive { get; } =
        Error.Validation("quantity.not_positive", "Quantity must be greater than zero.");
}

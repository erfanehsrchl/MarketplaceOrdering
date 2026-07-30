using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Money;

public static class CurrencyErrors
{
    public static Error InvalidCode { get; } =
        Error.Validation("currency.invalid_code", "Currency code must contain exactly three ASCII letters.");

    public static Error InvalidScale { get; } =
        Error.Validation("currency.invalid_scale", "Currency scale must be between zero and four.");
}

using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Money;

public sealed record Currency
{
    private Currency(string code, int scale)
    {
        Code = code;
        Scale = scale;
    }

    public string Code { get; }
    public int Scale { get; }

    public static Currency IRR { get; } = new("IRR", 0);
    public static Currency USD { get; } = new("USD", 2);
    public static Currency EUR { get; } = new("EUR", 2);
    public static Currency JPY { get; } = new("JPY", 0);
    public static Currency KWD { get; } = new("KWD", 3);

    public static Result<Currency> Create(string? code, int scale)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant();
        if (!IsValidCode(normalizedCode))
        {
            return Result<Currency>.Failure(CurrencyErrors.InvalidCode);
        }

        return scale is < 0 or > 4
            ? Result<Currency>.Failure(CurrencyErrors.InvalidScale)
            : Result<Currency>.Success(new Currency(normalizedCode!, scale));
    }

    public override string ToString() => Code;

    private static bool IsValidCode(string? code)
    {
        if (code is null || code.Length != 3)
        {
            return false;
        }

        foreach (var character in code)
        {
            if (character is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}

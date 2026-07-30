using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Money;

public sealed record Money
{
    private Money(long minorUnits, Currency currency)
    {
        MinorUnits = minorUnits;
        Currency = currency;
    }

    public long MinorUnits { get; }
    public Currency Currency { get; }
    public bool IsZero => MinorUnits == 0;

    public static Result<Money> Create(long minorUnits, Currency? currency)
    {
        if (minorUnits < 0)
        {
            return Result<Money>.Failure(MoneyErrors.Negative);
        }

        return currency is null
            ? Result<Money>.Failure(MoneyErrors.CurrencyRequired)
            : Result<Money>.Success(new Money(minorUnits, currency));
    }

    public static Money Zero(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(0, currency);
    }

    public Result<Money> Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            return Result<Money>.Failure(MoneyErrors.CurrencyMismatch);
        }

        try
        {
            return Result<Money>.Success(new Money(checked(MinorUnits + other.MinorUnits), Currency));
        }
        catch (OverflowException)
        {
            return Result<Money>.Failure(MoneyErrors.Overflow);
        }
    }

    public Result<Money> Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            return Result<Money>.Failure(MoneyErrors.CurrencyMismatch);
        }

        if (MinorUnits < other.MinorUnits)
        {
            return Result<Money>.Failure(MoneyErrors.InsufficientAmount);
        }

        try
        {
            return Result<Money>.Success(new Money(checked(MinorUnits - other.MinorUnits), Currency));
        }
        catch (OverflowException)
        {
            return Result<Money>.Failure(MoneyErrors.Overflow);
        }
    }

    public Result<int> CompareTo(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Currency != other.Currency
            ? Result<int>.Failure(MoneyErrors.CurrencyMismatch)
            : Result<int>.Success(MinorUnits.CompareTo(other.MinorUnits));
    }

    public override string ToString() => $"{MinorUnits} {Currency.Code}";
}

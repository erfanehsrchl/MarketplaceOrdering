using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Money;

public readonly record struct Money : IComparable<Money>
{
    private Money(long amount) => Amount = amount;

    public long Amount { get; }

    public static Money Zero { get; } = new(0);

    public static Result<Money> Create(long amount)
    {
        return amount < 0
            ? Result<Money>.Failure(MoneyErrors.Negative)
            : Result<Money>.Success(new Money(amount));
    }

    public Result<Money> Add(Money other)
    {
        try
        {
            return Result<Money>.Success(new Money(checked(Amount + other.Amount)));
        }
        catch (OverflowException)
        {
            return Result<Money>.Failure(MoneyErrors.Overflow);
        }
    }

    public Result<Money> Subtract(Money other)
    {
        if (Amount < other.Amount)
        {
            return Result<Money>.Failure(MoneyErrors.InsufficientAmount);
        }

        try
        {
            return Result<Money>.Success(new Money(checked(Amount - other.Amount)));
        }
        catch (OverflowException)
        {
            return Result<Money>.Failure(MoneyErrors.Overflow);
        }
    }

    public Result<Money> Multiply(int multiplier)
    {
        if (multiplier < 0)
        {
            return Result<Money>.Failure(MoneyErrors.NegativeMultiplier);
        }

        try
        {
            return Result<Money>.Success(
                new Money(checked(Amount * multiplier)));
        }
        catch (OverflowException)
        {
            return Result<Money>.Failure(MoneyErrors.Overflow);
        }
    }

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);
}

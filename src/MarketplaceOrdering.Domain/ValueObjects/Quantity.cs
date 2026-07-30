using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public readonly record struct Quantity
{
    private Quantity(int value) => Value = value;
    public int Value { get; }

    public static Result<Quantity> Create(int value) =>
        value <= 0
            ? Result<Quantity>.Failure(QuantityErrors.NotPositive)
            : Result<Quantity>.Success(new Quantity(value));

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

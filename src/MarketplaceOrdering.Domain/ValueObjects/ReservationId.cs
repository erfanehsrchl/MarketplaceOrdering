using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public readonly record struct ReservationId
{
    private ReservationId(Guid value) => Value = value;
    public Guid Value { get; }

    public static Result<ReservationId> Create(Guid value) =>
        value == Guid.Empty
            ? Result<ReservationId>.Failure(Error.Validation("reservation_id.empty", "Reservation identifier cannot be empty."))
            : Result<ReservationId>.Success(new ReservationId(value));

    public static ReservationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

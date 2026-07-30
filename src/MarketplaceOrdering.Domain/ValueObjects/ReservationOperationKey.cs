using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.ValueObjects;

public sealed record ReservationOperationKey
{
    private ReservationOperationKey(string value) => Value = value;
    public string Value { get; }

    public static Result<ReservationOperationKey> Create(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? Result<ReservationOperationKey>.Failure(
                Error.Validation("reservation_operation_key.empty", "Reservation operation key cannot be empty."))
            : Result<ReservationOperationKey>.Success(new ReservationOperationKey(trimmed));
    }

    public static ReservationOperationKey For(
        OrderId orderId,
        CheckoutAttemptId checkoutAttemptId,
        VendorId vendorId) =>
        new($"reservation:{orderId.Value:N}:{checkoutAttemptId.Value:N}:{vendorId.Value:N}");

    public override string ToString() => Value;
}

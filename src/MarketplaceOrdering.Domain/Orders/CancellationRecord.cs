using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Orders;

public sealed class CancellationRecord
{
    internal CancellationRecord(
        CancellationReason reason,
        DateTimeOffset cancelledAt,
        OrderStatus previousStatus)
    {
        Reason = reason;
        CancelledAt = cancelledAt;
        PreviousStatus = previousStatus;
    }

    public CancellationReason Reason { get; }
    public DateTimeOffset CancelledAt { get; }
    public OrderStatus PreviousStatus { get; }

    internal static CancellationRecord Rehydrate(
        CancellationReason reason,
        DateTimeOffset cancelledAt,
        OrderStatus previousStatus) =>
        new(reason, cancelledAt, previousStatus);
}

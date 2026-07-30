using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Payments;

namespace MarketplaceOrdering.Infrastructure.Persistence.InMemory;

internal static class OrderPersistenceSnapshotMapper
{
    internal static OrderPersistenceSnapshot Capture(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new OrderPersistenceSnapshot(
            order.Id,
            order.CustomerId,
            order.DeliveryAddress,
            order.Status,
            order.CreatedAt,
            order.Items.Select(item => new OrderItemSnapshot(
                item.ProductId, item.ProductName, item.Quantity)).ToArray(),
            order.SelectedDiscount,
            Capture(order.CheckoutAttempt),
            order.Payment is null
                ? null
                : new PaymentSnapshot(
                    order.Payment.TransactionId,
                    order.Payment.Amount,
                    order.Payment.PaidAt),
            order.Cancellation is null
                ? null
                : new CancellationSnapshot(
                    order.Cancellation.Reason,
                    order.Cancellation.CancelledAt,
                    order.Cancellation.PreviousStatus),
            order.ExpiredAt);
    }

    internal static Order Rehydrate(OrderPersistenceSnapshot snapshot)
    {
        var attempt = snapshot.CheckoutAttempt is null
            ? null
            : CheckoutAttempt.Rehydrate(
                snapshot.CheckoutAttempt.Id,
                snapshot.CheckoutAttempt.Status,
                snapshot.CheckoutAttempt.StartedAt,
                snapshot.CheckoutAttempt.FulfillmentPlan,
                snapshot.CheckoutAttempt.Reservations.Select(Rehydrate),
                snapshot.CheckoutAttempt.Failure,
                snapshot.CheckoutAttempt.CompletedAt,
                snapshot.CheckoutAttempt.PaymentExpiresAt);
        return Order.Rehydrate(
            snapshot.OrderId,
            snapshot.CustomerId,
            snapshot.DeliveryAddress,
            snapshot.Items.Select(item => OrderItem.Rehydrate(
                item.ProductId, item.ProductName, item.Quantity)),
            snapshot.Status,
            snapshot.CreatedAt,
            snapshot.SelectedDiscount,
            attempt,
            snapshot.Payment is null
                ? null
                : PaymentRecord.Rehydrate(
                    snapshot.Payment.TransactionId,
                    snapshot.Payment.Amount,
                    snapshot.Payment.PaidAt),
            snapshot.Cancellation is null
                ? null
                : CancellationRecord.Rehydrate(
                    snapshot.Cancellation.Reason,
                    snapshot.Cancellation.CancelledAt,
                    snapshot.Cancellation.PreviousStatus),
            snapshot.ExpiredAt);
    }

    private static CheckoutAttemptSnapshot? Capture(CheckoutAttempt? attempt) =>
        attempt is null
            ? null
            : new CheckoutAttemptSnapshot(
                attempt.Id,
                attempt.Status,
                attempt.StartedAt,
                attempt.FulfillmentPlan,
                attempt.Reservations.Select(reservation =>
                    new InventoryReservationSnapshot(
                        reservation.VendorId,
                        reservation.OperationKey,
                        reservation.Status,
                        reservation.RequestedAt,
                        reservation.ReservationId,
                        reservation.ReservedAt,
                        reservation.ExpiresAt,
                        reservation.ReleasedAt,
                        reservation.FailureCode,
                        reservation.ReleaseAttemptCount,
                        reservation.LastReleaseErrorCode,
                        reservation.LastReleaseAttemptedAt)).ToArray(),
                attempt.Failure,
                attempt.CompletedAt,
                attempt.PaymentExpiresAt);

    private static InventoryReservation Rehydrate(
        InventoryReservationSnapshot snapshot) =>
        InventoryReservation.Rehydrate(
            snapshot.VendorId,
            snapshot.OperationKey,
            snapshot.Status,
            snapshot.RequestedAt,
            snapshot.ReservationId,
            snapshot.ReservedAt,
            snapshot.ExpiresAt,
            snapshot.ReleasedAt,
            snapshot.FailureCode,
            snapshot.ReleaseAttemptCount,
            snapshot.LastReleaseErrorCode,
            snapshot.LastReleaseAttemptedAt);
}

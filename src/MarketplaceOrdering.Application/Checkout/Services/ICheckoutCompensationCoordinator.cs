using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Checkout.Services;

/// <summary>
/// Undoes the effects of a Checkout that cannot complete, and returns the Order
/// to Draft.
/// </summary>
/// <remarks>
/// <para>
/// Compensation lives in Application because it is I/O against an external
/// service; the Domain owns only the decisions — which Reservations need
/// releasing, and which state transitions are legal once they are gone.
/// Splitting it this way is what keeps the orchestrator readable: the happy path
/// stays a straight line, and every way it can go wrong is handled here.
/// </para>
/// <para>
/// Two situations are distinguished throughout. When the Order in memory is
/// still authoritative, compensation runs against it directly. When persistence
/// failed and the in-memory Order can no longer be trusted, the persisted state
/// is re-read first, and the attempt is best-effort: the Checkout has already
/// failed, so a failure to tidy up must not replace the error the caller needs
/// to see.
/// </para>
/// </remarks>
public interface ICheckoutCompensationCoordinator
{
    /// <summary>
    /// Fails an attempt that never confirmed a Reservation, so there is nothing
    /// to release.
    /// </summary>
    Task<Result> AbortBeforeReservationsAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        Error originalError,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases every confirmed Reservation, then returns the Order to Draft.
    /// Falls back to <see cref="AbortBeforeReservationsAsync"/> when nothing was
    /// confirmed.
    /// </summary>
    Task<Result> CompensateAsync(
        Order order,
        CheckoutAttemptId checkoutAttemptId,
        Error originalError,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads the Order and compensates whatever was actually persisted. Used
    /// after a save failure, when the in-memory Order and the store may disagree.
    /// </summary>
    Task ReconcilePersistedStateAsync(
        OrderId orderId,
        CheckoutAttemptId checkoutAttemptId,
        Error originalError,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads the Order and fails it back to Draft if, and only if, it is
    /// still the claimed Processing state this attempt left behind.
    /// </summary>
    Task AbortPersistedStateAsync(
        OrderId orderId,
        CheckoutAttemptId checkoutAttemptId,
        Error originalError,
        CancellationToken cancellationToken);

    /// <summary>
    /// Disposes of a Reservation the Inventory service confirmed but the Order
    /// never recorded.
    /// </summary>
    /// <remarks>
    /// Nothing in the Order points at this Reservation, so ordinary compensation
    /// can never find it. It is released immediately, and if that cannot be
    /// confirmed it is written to the recovery store, which is the only place it
    /// still exists. Failing to record it is the one outcome that genuinely
    /// loses stock, so it is reported rather than swallowed.
    /// </remarks>
    Task<Result> DiscardUnrecordedReservationAsync(
        OrderId orderId,
        CheckoutAttemptId checkoutAttemptId,
        VendorId vendorId,
        ReservationOperationKey operationKey,
        ReservationId reservationId,
        Error? persistenceError,
        CancellationToken cancellationToken);
}

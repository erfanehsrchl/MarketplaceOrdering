using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Common.Abstractions.Inventory;

public interface IInventoryReservationService
{
    Task<Result<InventoryReservationOutcome>> ReserveAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken);

    Task<Result<InventoryReleaseOutcome>> ReleaseAsync(
        InventoryReleaseRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads back the outcome the service recorded for a Reservation operation
    /// key, without creating anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Port contract: a service that dedupes on
    /// <c>ReservationOperationKey</c> already knows whether a given key produced
    /// a Reservation. Exposing that read is what turns an indeterminate
    /// <c>ReserveAsync</c> into a recoverable state instead of a permanent one:
    /// without it, an Order whose reservation call timed out can never learn
    /// whether stock was taken, and is stuck in <c>Processing</c> forever.
    /// </para>
    /// <para>
    /// Implementations must return <see cref="InventoryReservationRejected"/>
    /// when the key was never seen — that proves the reserve call never landed
    /// and no stock was taken. <see cref="InventoryReservationIndeterminate"/>
    /// is reserved for the case where the service itself cannot answer.
    /// </para>
    /// </remarks>
    Task<Result<InventoryReservationOutcome>> ResolveAsync(
        InventoryReservationQuery query,
        CancellationToken cancellationToken);
}

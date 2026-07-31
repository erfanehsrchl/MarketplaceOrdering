using MediatR;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Checkout.AbandonStuckCheckout;

/// <summary>
/// Releases an Order that a Checkout attempt claimed and then stopped
/// progressing on, returning it to Draft.
/// </summary>
public sealed record AbandonStuckCheckoutCommand(Guid OrderId)
    : IRequest<Result<AbandonStuckCheckoutResult>>;

/// <param name="ResolvedReservations">
/// How many Reservations whose outcome was unknown were read back from the
/// Inventory service during recovery.
/// </param>
/// <param name="PendingReleases">
/// How many Reservations still need a release retry after recovery ran.
/// </param>
public sealed record AbandonStuckCheckoutResult(
    Guid OrderId,
    string Status,
    Guid CheckoutAttemptId,
    int ResolvedReservations,
    int PendingReleases,
    long Version);

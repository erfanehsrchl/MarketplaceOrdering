namespace MarketplaceOrdering.Domain.Payments;

/// <summary>
/// Rules that constrain how much a payment provider's reported payment time is
/// allowed to differ from the marketplace's own authoritative confirmation time.
/// </summary>
/// <remarks>
/// <para>
/// <c>PaidAt</c> is reported by an external payment provider and is therefore
/// untrusted input. The Order still records it because it is the business fact
/// the provider settled on, but it can never be the value a security-relevant
/// decision is based on: an unbounded <c>PaidAt</c> would let a caller replay a
/// stale timestamp and pay for an Order whose Reservations already expired.
/// </para>
/// <para>
/// Reservations therefore expire against the marketplace clock, and the
/// reported time is only accepted inside a bounded window around it.
/// </para>
/// </remarks>
public static class PaymentPolicy
{
    /// <summary>
    /// How far ahead of the marketplace clock a reported payment time may be.
    /// Covers ordinary clock drift between the provider and the marketplace.
    /// </summary>
    public static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How far behind the marketplace clock a reported payment time may be.
    /// Bounded by the Reservation lifetime, because a payment reported as older
    /// than the whole payment window can never belong to a live Reservation.
    /// </summary>
    public static readonly TimeSpan MaximumReportingDelay =
        TimeSpan.FromMinutes(15);

    public static bool IsAcceptableReportedTime(
        DateTimeOffset paidAt,
        DateTimeOffset confirmedAt) =>
        paidAt <= confirmedAt + MaximumFutureSkew
        && paidAt >= confirmedAt - MaximumReportingDelay;
}

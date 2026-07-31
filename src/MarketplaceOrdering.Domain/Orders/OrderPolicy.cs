namespace MarketplaceOrdering.Domain.Orders;

/// <summary>
/// Time and size limits the Order Aggregate enforces.
/// </summary>
public static class OrderPolicy
{
    /// <summary>
    /// How long a Checkout attempt may stay in <see cref="OrderStatus.Processing"/>
    /// before a recovery use case is allowed to abandon it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Checkout claims the Order by moving it to <c>Processing</c> and saving
    /// before any external call, which is what makes concurrent Checkouts
    /// impossible. The cost of that claim is that a process crash, or an
    /// Inventory call whose outcome never came back, leaves the Order claimed
    /// with nobody to release it. Without a timeout that state is permanent:
    /// the Order can never be edited or checked out again, and its idempotency
    /// key stays <c>InProgress</c> forever.
    /// </para>
    /// <para>
    /// The value is generous relative to a Checkout that only makes a handful of
    /// port calls, so a merely slow Checkout is never abandoned underneath
    /// itself. Even if it were, abandoning is safe: recovery re-reads every
    /// Reservation outcome from the Inventory service before releasing, and the
    /// Order's optimistic version makes the racing writer lose.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan CheckoutAttemptTimeout =
        TimeSpan.FromMinutes(5);
}

namespace MarketplaceOrdering.Domain.Fulfillment;

/// <summary>
/// The limits the fulfillment search runs under.
/// </summary>
/// <param name="MaxVendorsPerOrder">
/// How many Vendors one Order may be split across.
/// </param>
/// <param name="MaxVendorsPerProduct">
/// How many Vendors one Product may be split across. Raising this alone does not
/// widen the search: <see cref="ProductAllocationGenerator"/> only produces
/// single-Vendor and two-Vendor splits, and <c>Order.AttachFulfillmentPlan</c>
/// independently rejects a Plan that splits a Product further. The value is used
/// for the pre-search stock check, so lowering it below 2 tightens that check
/// correctly while raising it above 2 would make the check optimistic.
/// </param>
/// <param name="MaxSearchNodes">
/// Hard ceiling on search work, counted in Vendor subsets examined plus
/// allocation branches expanded.
/// </param>
/// <remarks>
/// <para>
/// The search is exact, so its cost grows with the number of Vendor subsets and
/// with the ways each Product can be split inside a subset. For real carts that
/// is instant. The ceiling exists so a pathological input degrades into a clean,
/// deterministic failure instead of an unbounded request.
/// </para>
/// <para>
/// The planner never returns a plan it could not prove optimal: exhausting the
/// budget is reported as <see cref="FulfillmentErrors.SearchBudgetExceeded"/>
/// rather than silently answering with the best plan found so far. Swapping in
/// an approximate strategy is a decision for whoever configures the planner, not
/// something the planner should do behind the caller's back.
/// </para>
/// </remarks>
public sealed record FulfillmentPlannerOptions(
    int MaxVendorsPerOrder,
    int MaxVendorsPerProduct,
    int MaxSearchNodes)
{
    public static FulfillmentPlannerOptions Default { get; } =
        new(MaxVendorsPerOrder: 3,
            MaxVendorsPerProduct: 2,
            MaxSearchNodes: 2_000_000);
}

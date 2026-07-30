namespace MarketplaceOrdering.Domain.Fulfillment;

internal sealed class FulfillmentPlanComparer : IComparer<FulfillmentCandidate>
{
    internal static FulfillmentPlanComparer Instance { get; } = new();

    public int Compare(FulfillmentCandidate? x, FulfillmentCandidate? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var comparison = x.TotalPayable.Amount.CompareTo(y.TotalPayable.Amount);
        if (comparison != 0) return comparison;
        comparison = x.Vendors.Count.CompareTo(y.Vendors.Count);
        if (comparison != 0) return comparison;
        comparison = x.MaximumDeliveryHours.CompareTo(y.MaximumDeliveryHours);
        return comparison != 0 ? comparison : CompareAllocationKey(x, y);
    }

    internal int CompareAllocationKey(
        FulfillmentCandidate x,
        FulfillmentCandidate y)
    {
        var left = x.Allocations.ToArray();
        var right = y.Allocations.ToArray();
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var comparison = left[index].VendorId.Value.CompareTo(
                right[index].VendorId.Value);
            if (comparison != 0) return comparison;
            comparison = left[index].ProductId.Value.CompareTo(
                right[index].ProductId.Value);
            if (comparison != 0) return comparison;
            comparison = left[index].Quantity.Value.CompareTo(
                right[index].Quantity.Value);
            if (comparison != 0) return comparison;
            comparison = left[index].UnitPrice.Amount.CompareTo(
                right[index].UnitPrice.Amount);
            if (comparison != 0) return comparison;
        }

        return left.Length.CompareTo(right.Length);
    }
}

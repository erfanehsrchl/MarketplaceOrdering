using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Orders;

public static class CancellationErrors
{
    public static Error NotAllowed { get; } = Error.BusinessRule(
        "cancellation.not_allowed",
        "Cancellation is not allowed in the current Order state.");
}

using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Orders;

public static class ExpirationErrors
{
    public static Error NotAllowed { get; } = Error.BusinessRule(
        "expiration.not_allowed",
        "Expiration is not allowed in the current Order state.");
    public static Error NotDue { get; } = Error.BusinessRule(
        "expiration.not_due", "Order payment has not expired yet.");
    public static Error PaymentExpirationMissing { get; } = Error.BusinessRule(
        "expiration.payment_expiration_missing",
        "Order payment expiration is missing.");
}

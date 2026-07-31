using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Fulfillment;

public static class FulfillmentErrors
{
    public static Error DemandsRequired { get; } = Error.Validation(
        "fulfillment.demands_required", "At least one product demand is required.");
    public static Error DuplicateProductDemand { get; } = Error.Validation(
        "fulfillment.duplicate_product_demand", "Product demands must be unique.");
    public static Error InvalidDeliveryHours { get; } = Error.Validation(
        "fulfillment.invalid_delivery_hours", "Estimated delivery hours must be positive.");
    public static Error DuplicateOffer { get; } = Error.Validation(
        "fulfillment.duplicate_offer", "A usable Vendor and Product offer must be unique.");
    public static Error InconsistentVendorTerms { get; } = Error.Validation(
        "fulfillment.inconsistent_vendor_terms", "Vendor shipping and minimum-order terms must be consistent.");
    public static Error NoValidPlan { get; } = Error.BusinessRule(
        "fulfillment.no_valid_plan", "No complete valid fulfillment plan exists.");
    public static Error CalculationOverflow { get; } = Error.BusinessRule(
        "fulfillment.calculation_overflow", "Fulfillment monetary calculation overflowed.");
    public static Error InvalidAllocation { get; } = Error.BusinessRule(
        "fulfillment.invalid_allocation", "The product allocation is invalid.");
    // Not a BusinessRule: the Order is perfectly valid and the customer did
    // nothing wrong — the search simply could not prove an optimum in time.
    public static Error SearchBudgetExceeded { get; } = Error.CapacityExceeded(
        "fulfillment.search_budget_exceeded",
        "The fulfillment search exceeded its configured work limit before it could prove an optimal plan.");
}

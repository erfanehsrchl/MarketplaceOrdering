using System.Globalization;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Discounts;

public static class DiscountErrors
{
    public static Error PercentageNotPositive { get; } =
        Error.Validation(
            "discount.percentage_not_positive",
            "Discount percentage must be greater than zero.");

    public static Error PercentageExceedsMaximum { get; } =
        Error.Validation(
            "discount.percentage_exceeds_maximum",
            $"Discount percentage cannot exceed {PercentageDiscountValue.MaximumPercentage}.");

    public static Error FixedAmountNotPositive { get; } =
        Error.Validation(
            "discount.fixed_amount_not_positive",
            "Fixed discount amount must be greater than zero.");

    public static Error CodeRequired { get; } =
        Error.Validation("discount.code_required", "Discount code is required.");

    public static Error ValueRequired { get; } =
        Error.Validation("discount.value_required", "Discount value is required.");

    public static Error InvalidDateRange { get; } =
        Error.Validation(
            "discount.invalid_date_range",
            "Discount policy start time cannot be later than its end time.");

    public static Error MaximumAmountNotPositive { get; } =
        Error.Validation(
            "discount.maximum_amount_not_positive",
            "Maximum discount amount must be greater than zero.");

    public static Error VendorAmountsRequired { get; } =
        Error.Validation(
            "discount.vendor_amounts_required",
            "At least one Vendor product amount is required.");

    public static Error DuplicateVendor(VendorId vendorId) =>
        Error.Validation(
            "discount.duplicate_vendor",
            "A Vendor may occur only once in a discount evaluation context.",
            new Dictionary<string, string>
            {
                ["vendorId"] = vendorId.ToString()
            });

    public static Error InconsistentTotalProductsAmount(
        long expectedAmount,
        long actualAmount) =>
        Error.Validation(
            "discount.inconsistent_total_products_amount",
            "Vendor product amounts must sum exactly to the total product amount.",
            new Dictionary<string, string>
            {
                ["expectedAmount"] =
                    expectedAmount.ToString(CultureInfo.InvariantCulture),
                ["actualAmount"] =
                    actualAmount.ToString(CultureInfo.InvariantCulture)
            });

    public static Error Inactive { get; } =
        Error.BusinessRule("discount.inactive", "The discount policy is inactive.");

    public static Error NotStarted { get; } =
        Error.BusinessRule(
            "discount.not_started",
            "The discount policy has not started.");

    public static Error Expired { get; } =
        Error.BusinessRule("discount.expired", "The discount policy has expired.");

    public static Error MinimumAmountNotMet(
        long requiredAmount,
        long actualAmount) =>
        Error.BusinessRule(
            "discount.minimum_amount_not_met",
            "The minimum product amount has not been met.",
            new Dictionary<string, string>
            {
                ["requiredAmount"] =
                    requiredAmount.ToString(CultureInfo.InvariantCulture),
                ["actualAmount"] =
                    actualAmount.ToString(CultureInfo.InvariantCulture)
            });

    public static Error NotApplicable { get; } =
        Error.BusinessRule(
            "discount.not_applicable",
            "The discount policy does not apply to any Vendor.");

    public static Error CalculationOverflow { get; } =
        Error.BusinessRule(
            "discount.calculation_overflow",
            "The discount calculation exceeded the supported range.");

    public static Error AllocationFailed { get; } =
        Error.BusinessRule(
            "discount.allocation_failed",
            "The discount could not be allocated while preserving its invariants.");
}

using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Checkout;

public static class CheckoutErrors
{
    private static Error Rule(string code, string message) =>
        Error.BusinessRule(code, message);
    public static Error FailureCodeRequired { get; } = Error.Validation("checkout.failure_code_required", "A valid failure code is required.");
    public static Error NotAllowed { get; } = Rule("checkout.not_allowed", "Checkout is not allowed in the current Order state.");
    public static Error AlreadyInProgress { get; } = Rule("checkout.already_in_progress", "Checkout is already in progress.");
    public static Error CompensationPending { get; } = Rule("checkout.compensation_pending", "Reservation compensation remains pending.");
    public static Error AttemptNotFound { get; } = Rule("checkout.attempt_not_found", "Checkout attempt was not found.");
    public static Error AttemptMismatch { get; } = Rule("checkout.attempt_mismatch", "Checkout attempt does not match.");
    public static Error InvalidAttemptState { get; } = Rule("checkout.invalid_attempt_state", "Checkout attempt is in an invalid state.");
    public static Error PlanRequired { get; } = Rule("checkout.plan_required", "A Fulfillment Plan is required.");
    public static Error PlanAlreadyAttached { get; } = Rule("checkout.plan_already_attached", "A Fulfillment Plan is already attached.");
    public static Error PlanDoesNotMatchOrder { get; } = Rule("checkout.plan_does_not_match_order", "Fulfillment Plan does not match Order demand.");
    public static Error VendorNotInPlan { get; } = Rule("checkout.vendor_not_in_plan", "Vendor is not in the Fulfillment Plan.");
    public static Error InvalidReservationOperationKey { get; } = Rule("checkout.invalid_reservation_operation_key", "Reservation operation key is invalid.");
    public static Error ReservationAlreadyExists { get; } = Rule("checkout.reservation_already_exists", "Reservation intent already exists.");
    public static Error ReservationNotFound { get; } = Rule("checkout.reservation_not_found", "Inventory Reservation was not found.");
    public static Error ReservationIdConflict { get; } = Rule("checkout.reservation_id_conflict", "Reservation identifier conflicts with the recorded result.");
    public static Error ReservationInvalidState { get; } = Rule("checkout.reservation_invalid_state", "Inventory Reservation is in an invalid state.");
    public static Error InvalidReservationExpiration { get; } = Rule("checkout.invalid_reservation_expiration", "Reservation expiration could not be calculated.");
    public static Error ReservationsIncomplete { get; } = Rule("checkout.reservations_incomplete", "Required Reservations are incomplete.");
    public static Error ReservationExpired { get; } = Rule("checkout.reservation_expired", "A required Reservation has expired.");
    public static Error CompensationRequired { get; } = Rule("checkout.compensation_required", "Confirmed Reservations require compensation.");
    public static Error CompensationNotComplete { get; } = Rule("checkout.compensation_not_complete", "Reservation compensation is not complete.");
    public static Error FailureRequired { get; } = Rule("checkout.failure_required", "A Checkout failure is required.");
}

using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Domain.Payments;

public static class PaymentErrors
{
    public static Error NotAllowed { get; } = Error.BusinessRule(
        "payment.not_allowed", "Payment is not allowed in the current Order state.");
    public static Error AmountMismatch { get; } = Error.BusinessRule(
        "payment.amount_mismatch", "Payment amount does not match the amount due.");
    public static Error ReservationsInvalid { get; } = Error.BusinessRule(
        "payment.reservations_invalid", "Required Reservations are not valid for payment.");
    public static Error ReservationExpired { get; } = Error.BusinessRule(
        "payment.reservation_expired", "A required Reservation has expired.");
    public static Error AlreadyConfirmedWithDifferentData { get; } = Error.Conflict(
        "payment.already_confirmed_with_different_data",
        "Payment was already confirmed with different data.");
    public static Error TransactionIdAlreadyUsed { get; } = Error.Conflict(
        "payment.transaction_id_already_used",
        "Transaction identifier is already used by another Order.");
    public static Error AmountNotPositive { get; } = Error.BusinessRule(
        "payment.amount_not_positive", "Confirmed payment amount must be positive.");
    public static Error ReportedTimeNotAcceptable { get; } = Error.BusinessRule(
        "payment.reported_time_not_acceptable",
        "The reported payment time is outside the accepted window around the marketplace clock.");
}

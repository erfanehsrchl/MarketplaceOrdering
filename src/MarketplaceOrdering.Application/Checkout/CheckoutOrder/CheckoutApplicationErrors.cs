using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Checkout.CheckoutOrder;

public static class CheckoutApplicationErrors
{
    public static Error IdempotencyInProgress(
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Error.Conflict(
            "checkout.idempotency_in_progress",
            "Checkout is already in progress for this idempotency key.",
            metadata);

    public static Error IdempotencyConflict(
        IReadOnlyDictionary<string, string> metadata) =>
        Error.Conflict(
            "checkout.idempotency_conflict",
            "The idempotency key belongs to another checkout.",
            metadata);

    public static Error IdempotencyFinalizationFailed(
        IReadOnlyDictionary<string, string> metadata) =>
        Error.DependencyFailure(
            "checkout.idempotency_finalization_failed",
            "Checkout state was persisted but idempotency finalization failed.",
            metadata);

    public static Error ReservationOutcomeIndeterminate(
        IReadOnlyDictionary<string, string> metadata) =>
        Error.DependencyFailure(
            "checkout.reservation_outcome_indeterminate",
            "The Inventory Reservation outcome is indeterminate.",
            metadata);

    public static Error ReservationPersistenceFailed(
        IReadOnlyDictionary<string, string> metadata) =>
        Error.Concurrency(
            "checkout.reservation_persistence_failed",
            "A successful Inventory Reservation could not be persisted.",
            metadata);

    public static Error CompensationPersistenceFailed(
        IReadOnlyDictionary<string, string> metadata) =>
        Error.Concurrency(
            "checkout.compensation_persistence_failed",
            "Checkout compensation state could not be persisted.",
            metadata);

    public static Error RecoveryRecordFailed(
        IReadOnlyDictionary<string, string> metadata) =>
        Error.DependencyFailure(
            "checkout.recovery_record_failed",
            "The orphan Reservation recovery record could not be persisted.",
            metadata);
}

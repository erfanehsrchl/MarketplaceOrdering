using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Infrastructure.Errors;

public static class InfrastructureErrors
{
    public static Error DiscountPolicyNotFound { get; } = Error.NotFound(
        "discount.policy_not_found", "Discount policy was not found.");
    public static Error InventoryInvalidRequest { get; } = Error.Validation(
        "inventory.invalid_request", "Inventory request is invalid.");
    public static Error InventoryDuplicateProduct { get; } = Error.Validation(
        "inventory.duplicate_product", "Inventory request contains a duplicate product.");
    public static Error InventoryOperationKeyConflict { get; } = Error.Conflict(
        "inventory.operation_key_conflict", "Reservation operation key was reused for a different request.");
    public static Error InventoryReservationNotFound { get; } = Error.NotFound(
        "inventory.reservation_not_found", "Inventory reservation was not found.");
    public static Error InventoryReleaseRequestConflict { get; } = Error.Conflict(
        "inventory.release_request_conflict", "Inventory release request does not match the reservation.");
    public static Error IdempotencyEntryNotFound { get; } = Error.NotFound(
        "idempotency.entry_not_found", "Idempotency entry was not found.");
    public static Error IdempotencyEntryConflict { get; } = Error.Conflict(
        "idempotency.entry_conflict", "Idempotency entry conflicts with the supplied result.");
    public static Error IdempotencyInvalidTransition { get; } = Error.Conflict(
        "idempotency.invalid_transition", "Idempotency entry cannot make the requested transition.");
    public static Error RecoveryMaximumCountInvalid { get; } = Error.Validation(
        "recovery.maximum_count_invalid", "Maximum count must be greater than zero.");
    public static Error RecoveryRecordConflict { get; } = Error.Conflict(
        "recovery.record_conflict", "Recovery operation key belongs to a different reservation.");
}

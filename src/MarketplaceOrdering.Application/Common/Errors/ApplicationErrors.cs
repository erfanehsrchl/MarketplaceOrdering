using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Common.Errors;

public static class ApplicationErrors
{
    public static Error OrderNotFound { get; } =
        Error.NotFound("order.not_found", "Order was not found.");
    public static Error OrderAlreadyExists { get; } =
        Error.Conflict("order.already_exists", "Order already exists.");
    public static Error OrderVersionConflict { get; } =
        Error.Concurrency("order.version_conflict", "Order was changed concurrently.");
    public static Error InvalidRequest { get; } =
        Error.Validation("application.invalid_request", "The request is invalid.");
    public static Error DependencyOperationFailed { get; } =
        Error.DependencyFailure("dependency.operation_failed", "A dependency operation failed.");
    public static Error DependencyOperationIndeterminate { get; } =
        Error.DependencyFailure("dependency.operation_indeterminate", "A dependency operation result is indeterminate.");
}

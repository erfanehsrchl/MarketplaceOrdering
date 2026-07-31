using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Application.Checkout.CheckoutOrder;

/// <summary>
/// Rebuilds the original <see cref="Error"/> from the failure code an Order
/// recorded when its Checkout failed.
/// </summary>
/// <remarks>
/// <para>
/// The Order stores only a code, because an Aggregate has no business holding an
/// Application error object. But a replay has to answer with the same error the
/// original run produced — including its <see cref="ErrorType"/>, which is what
/// decides the HTTP status. A client that got a 422 the first time must not get
/// a 503 for the identical request.
/// </para>
/// <para>
/// Unknown codes fall back to a dependency failure rather than throwing: they
/// come from an external service and the set is open-ended, so treating an
/// unrecognised one as a retryable dependency problem is both truthful and safe.
/// </para>
/// </remarks>
public static class CheckoutFailureRehydrator
{
    private static readonly Error[] KnownErrors =
    [
        CheckoutErrors.NotAllowed,
        CheckoutErrors.AlreadyInProgress,
        CheckoutErrors.CompensationPending,
        CheckoutErrors.AttemptNotFound,
        CheckoutErrors.AttemptMismatch,
        CheckoutErrors.InvalidAttemptState,
        CheckoutErrors.PlanRequired,
        CheckoutErrors.PlanAlreadyAttached,
        CheckoutErrors.PlanDoesNotMatchOrder,
        CheckoutErrors.VendorNotInPlan,
        CheckoutErrors.InvalidReservationOperationKey,
        CheckoutErrors.ReservationAlreadyExists,
        CheckoutErrors.ReservationNotFound,
        CheckoutErrors.ReservationIdConflict,
        CheckoutErrors.ReservationInvalidState,
        CheckoutErrors.InvalidReservationExpiration,
        CheckoutErrors.ReservationsIncomplete,
        CheckoutErrors.ReservationExpired,
        CheckoutErrors.CompensationRequired,
        CheckoutErrors.CompensationNotComplete,
        CheckoutErrors.FailureRequired,
        CheckoutErrors.NotStuck,
        CheckoutErrors.AbandonedAfterTimeout,
        ApplicationErrors.OrderNotFound,
        ApplicationErrors.OrderAlreadyExists,
        ApplicationErrors.OrderVersionConflict,
        ApplicationErrors.InvalidRequest,
        ApplicationErrors.DependencyOperationFailed,
        ApplicationErrors.DependencyOperationIndeterminate
    ];

    public static Error Rehydrate(string code) =>
        KnownErrors.FirstOrDefault(error => error.Code == code)
        ?? Error.DependencyFailure(code, "Checkout previously failed.");
}

using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Checkout;

public sealed class CheckoutAttempt
{
    private readonly List<InventoryReservation> _reservations = [];
    private CheckoutAttempt(CheckoutAttemptId id, DateTimeOffset startedAt)
    { Id = id; StartedAt = startedAt; Status = CheckoutAttemptStatus.Planning; }
    public CheckoutAttemptId Id { get; }
    public CheckoutAttemptStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public FulfillmentPlan? FulfillmentPlan { get; private set; }
    public IReadOnlyCollection<InventoryReservation> Reservations => _reservations.ToArray();
    public CheckoutFailure? Failure { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? PaymentExpiresAt { get; private set; }

    internal static CheckoutAttempt Create(CheckoutAttemptId id, DateTimeOffset at) => new(id, at);
    internal static CheckoutAttempt Rehydrate(
        CheckoutAttemptId id,
        CheckoutAttemptStatus status,
        DateTimeOffset startedAt,
        FulfillmentPlan? fulfillmentPlan,
        IEnumerable<InventoryReservation> reservations,
        CheckoutFailure? failure,
        DateTimeOffset? completedAt,
        DateTimeOffset? paymentExpiresAt)
    {
        var attempt = new CheckoutAttempt(id, startedAt)
        {
            Status = status,
            FulfillmentPlan = fulfillmentPlan,
            Failure = failure,
            CompletedAt = completedAt,
            PaymentExpiresAt = paymentExpiresAt
        };
        attempt._reservations.AddRange(reservations);
        return attempt;
    }
    internal Result AttachPlan(FulfillmentPlan plan)
    {
        if (FulfillmentPlan is not null) return Result.Failure(CheckoutErrors.PlanAlreadyAttached);
        if (Status != CheckoutAttemptStatus.Planning) return Result.Failure(CheckoutErrors.InvalidAttemptState);
        FulfillmentPlan = plan; Status = CheckoutAttemptStatus.Reserving; return Result.Success();
    }
    internal InventoryReservation? Find(ReservationOperationKey key) =>
        _reservations.FirstOrDefault(r => r.OperationKey == key);
    internal InventoryReservation? Find(ReservationId id) =>
        _reservations.FirstOrDefault(r => r.ReservationId == id);
    internal void Add(InventoryReservation reservation) => _reservations.Add(reservation);
    internal void RefreshReservationStatus()
    {
        if (FulfillmentPlan is not null
            && _reservations.Count == FulfillmentPlan.VendorCount
            && _reservations.All(r => r.Status == InventoryReservationStatus.Active)
            && _reservations.Select(r => r.VendorId).ToHashSet()
                .SetEquals(FulfillmentPlan.Vendors.Select(v => v.VendorId)))
            Status = CheckoutAttemptStatus.FullyReserved;
    }
    internal Result SetFailure(CheckoutFailure failure)
    {
        if (Failure is not null && Failure.Code != failure.Code)
            return Result.Failure(CheckoutErrors.InvalidAttemptState);
        Failure ??= failure; Status = CheckoutAttemptStatus.Compensating;
        return Result.Success();
    }
    internal void FinalizeFailure(bool pending)
    { Status = pending ? CheckoutAttemptStatus.CompensationPending : CheckoutAttemptStatus.Failed; }
    internal void CompleteCompensation() => Status = CheckoutAttemptStatus.Failed;
    internal void Complete(DateTimeOffset completedAt, DateTimeOffset expiresAt)
    { Status = CheckoutAttemptStatus.Completed; CompletedAt = completedAt; PaymentExpiresAt = expiresAt; }
}

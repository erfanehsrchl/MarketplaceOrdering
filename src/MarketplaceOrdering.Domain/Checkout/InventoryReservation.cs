using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Checkout;

public sealed class InventoryReservation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private InventoryReservation(VendorId vendorId, ReservationOperationKey key, DateTimeOffset requestedAt)
    { VendorId = vendorId; OperationKey = key; RequestedAt = requestedAt; Status = InventoryReservationStatus.Pending; }
    public VendorId VendorId { get; }
    public ReservationOperationKey OperationKey { get; }
    public InventoryReservationStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; }
    public ReservationId? ReservationId { get; private set; }
    public DateTimeOffset? ReservedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public int ReleaseAttemptCount { get; private set; }
    public string? LastReleaseErrorCode { get; private set; }
    public DateTimeOffset? LastReleaseAttemptedAt { get; private set; }

    internal static Result<InventoryReservation> CreatePending(VendorId vendorId, ReservationOperationKey key, DateTimeOffset requestedAt) =>
        Result<InventoryReservation>.Success(new InventoryReservation(vendorId, key, requestedAt));

    internal Result<bool> MarkActive(ReservationId reservationId, DateTimeOffset reservedAt)
    {
        if (Status == InventoryReservationStatus.Active)
            return ReservationId == reservationId && ReservedAt == reservedAt
                ? Result<bool>.Success(false)
                : Result<bool>.Failure(CheckoutErrors.ReservationIdConflict);
        if (Status != InventoryReservationStatus.Pending)
            return Result<bool>.Failure(CheckoutErrors.ReservationInvalidState);
        DateTimeOffset expiresAt;
        try { expiresAt = reservedAt.Add(Lifetime); }
        catch (ArgumentOutOfRangeException) { return Result<bool>.Failure(CheckoutErrors.InvalidReservationExpiration); }
        ReservationId = reservationId; ReservedAt = reservedAt; ExpiresAt = expiresAt;
        Status = InventoryReservationStatus.Active;
        return Result<bool>.Success(true);
    }

    internal Result<bool> MarkRejected(string failureCode)
    {
        var normalized = CheckoutFailure.Create(failureCode, RequestedAt);
        if (normalized.IsFailure) return Result<bool>.Failure(normalized.Error);
        if (Status == InventoryReservationStatus.Rejected)
            return FailureCode == normalized.Value.Code ? Result<bool>.Success(false)
                : Result<bool>.Failure(CheckoutErrors.ReservationInvalidState);
        if (Status != InventoryReservationStatus.Pending)
            return Result<bool>.Failure(CheckoutErrors.ReservationInvalidState);
        FailureCode = normalized.Value.Code; Status = InventoryReservationStatus.Rejected;
        return Result<bool>.Success(true);
    }

    internal Result<bool> MarkReleased(DateTimeOffset releasedAt)
    {
        if (Status == InventoryReservationStatus.Released) return Result<bool>.Success(false);
        if (Status is not (InventoryReservationStatus.Active or InventoryReservationStatus.ReleasePending))
            return Result<bool>.Failure(CheckoutErrors.ReservationInvalidState);
        Status = InventoryReservationStatus.Released; ReleasedAt = releasedAt;
        return Result<bool>.Success(true);
    }

    internal Result MarkReleasePending(string errorCode, DateTimeOffset attemptedAt)
    {
        var normalized = CheckoutFailure.Create(errorCode, attemptedAt);
        if (normalized.IsFailure) return Result.Failure(normalized.Error);
        if (Status is not (InventoryReservationStatus.Active or InventoryReservationStatus.ReleasePending))
            return Result.Failure(CheckoutErrors.ReservationInvalidState);
        Status = InventoryReservationStatus.ReleasePending;
        ReleaseAttemptCount++; LastReleaseErrorCode = normalized.Value.Code;
        LastReleaseAttemptedAt = attemptedAt;
        return Result.Success();
    }
}

using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Recovery;

/// <summary>
/// An Inventory Reservation the external service confirmed but the Order never
/// persisted, so no Aggregate can point at it. Recovery therefore needs its own
/// durable record instead of Aggregate state.
/// </summary>
/// <param name="CreatedAt">
/// When the orphan was first observed. Never advances, so the queue keeps a
/// stable age ordering.
/// </param>
/// <param name="AttemptCount">
/// How many release attempts have run, for backoff and alerting thresholds.
/// </param>
/// <param name="LastAttemptedAt">
/// When the most recent release attempt ran. Advances on every attempt and is
/// what a backoff policy schedules against.
/// </param>
public sealed record ReservationRecoveryRecord(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationOperationKey OperationKey,
    ReservationId ReservationId,
    string LastErrorCode,
    DateTimeOffset CreatedAt,
    int AttemptCount,
    DateTimeOffset LastAttemptedAt);

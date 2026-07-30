using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Common.Abstractions.Recovery;

public sealed record ReservationRecoveryRecord(
    OrderId OrderId,
    CheckoutAttemptId CheckoutAttemptId,
    VendorId VendorId,
    ReservationOperationKey OperationKey,
    ReservationId ReservationId,
    string LastErrorCode,
    DateTimeOffset CreatedAt,
    int AttemptCount);

using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Infrastructure.Inventory;

public enum InMemoryReservationBehaviorKind
{
    Normal,
    Reject,
    Indeterminate,
    ReturnResultFailure
}

public sealed record InMemoryReservationBehavior
{
    private InMemoryReservationBehavior(
        InMemoryReservationBehaviorKind kind,
        string? failureCode,
        Error? error)
    {
        Kind = kind;
        FailureCode = failureCode;
        Error = error;
    }

    public InMemoryReservationBehaviorKind Kind { get; }
    public string? FailureCode { get; }
    public Error? Error { get; }
    public static InMemoryReservationBehavior Normal { get; } =
        new(InMemoryReservationBehaviorKind.Normal, null, null);
    public static InMemoryReservationBehavior Reject(string failureCode) =>
        WithCode(InMemoryReservationBehaviorKind.Reject, failureCode);
    public static InMemoryReservationBehavior Indeterminate(string failureCode) =>
        WithCode(InMemoryReservationBehaviorKind.Indeterminate, failureCode);
    public static InMemoryReservationBehavior ReturnResultFailure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(InMemoryReservationBehaviorKind.ReturnResultFailure, null, error);
    }

    private static InMemoryReservationBehavior WithCode(
        InMemoryReservationBehaviorKind kind,
        string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return new(kind, failureCode.Trim(), null);
    }
}

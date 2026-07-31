using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Infrastructure.Inventory;

/// <summary>
/// The distinct ways a reservation call can go wrong, kept separate because
/// recovery depends on which one happened.
/// </summary>
public enum InMemoryReservationBehaviorKind
{
    Normal,

    /// <summary>Definitive refusal. Nothing is held.</summary>
    Reject,

    /// <summary>
    /// The request never reached the service: no stock moves and no operation
    /// key is recorded, so a later lookup can prove nothing was held.
    /// </summary>
    Indeterminate,

    /// <summary>
    /// The reservation really happened but its response was lost. Stock is
    /// held and the operation key is recorded, while the caller is told the
    /// outcome is unknown. This is the case that leaks stock unless recovery
    /// reads the outcome back.
    /// </summary>
    LostResponse,

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
    public static InMemoryReservationBehavior LostResponse(string failureCode) =>
        WithCode(InMemoryReservationBehaviorKind.LostResponse, failureCode);
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

using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Infrastructure.Inventory;

public enum InMemoryReleaseBehaviorKind
{
    Normal,
    Fail,
    Indeterminate,
    ReturnResultFailure
}

public sealed record InMemoryReleaseBehavior
{
    private InMemoryReleaseBehavior(
        InMemoryReleaseBehaviorKind kind,
        string? errorCode,
        Error? error)
    {
        Kind = kind;
        ErrorCode = errorCode;
        Error = error;
    }

    public InMemoryReleaseBehaviorKind Kind { get; }
    public string? ErrorCode { get; }
    public Error? Error { get; }
    public static InMemoryReleaseBehavior Normal { get; } =
        new(InMemoryReleaseBehaviorKind.Normal, null, null);
    public static InMemoryReleaseBehavior Fail(string errorCode) =>
        WithCode(InMemoryReleaseBehaviorKind.Fail, errorCode);
    public static InMemoryReleaseBehavior Indeterminate(string errorCode) =>
        WithCode(InMemoryReleaseBehaviorKind.Indeterminate, errorCode);
    public static InMemoryReleaseBehavior ReturnResultFailure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(InMemoryReleaseBehaviorKind.ReturnResultFailure, null, error);
    }

    private static InMemoryReleaseBehavior WithCode(
        InMemoryReleaseBehaviorKind kind,
        string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new(kind, errorCode.Trim(), null);
    }
}

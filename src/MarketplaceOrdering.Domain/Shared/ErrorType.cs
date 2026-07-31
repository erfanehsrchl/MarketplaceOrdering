namespace MarketplaceOrdering.Domain.Shared;

/// <summary>
/// What kind of failure an <see cref="Error"/> represents.
/// </summary>
/// <remarks>
/// The type carries the one thing a caller most needs: whose problem this is and
/// whether retrying could help. Transport layers translate it into their own
/// vocabulary, which is why the Domain never mentions HTTP.
/// </remarks>
public enum ErrorType
{
    None = 0,

    /// <summary>Malformed input. Retrying unchanged cannot help.</summary>
    Validation = 1,

    /// <summary>The thing referred to does not exist.</summary>
    NotFound = 2,

    /// <summary>Well-formed, but a business rule forbids it.</summary>
    BusinessRule = 3,

    /// <summary>Conflicts with state that already exists.</summary>
    Conflict = 4,

    /// <summary>Lost a race; reloading and retrying may succeed.</summary>
    Concurrency = 5,

    /// <summary>An external service failed or gave no usable answer.</summary>
    DependencyFailure = 6,

    /// <summary>
    /// The request is valid and nothing failed, but the work could not be
    /// completed within the limits this system runs under. Not the caller's
    /// fault, and distinct from a dependency failure because nothing external
    /// was involved.
    /// </summary>
    CapacityExceeded = 7
}

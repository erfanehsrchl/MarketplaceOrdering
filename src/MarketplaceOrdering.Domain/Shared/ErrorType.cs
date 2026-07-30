namespace MarketplaceOrdering.Domain.Shared;

public enum ErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    BusinessRule = 3,
    Conflict = 4,
    Concurrency = 5,
    DependencyFailure = 6
}

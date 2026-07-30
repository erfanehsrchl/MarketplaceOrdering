namespace MarketplaceOrdering.Domain.Shared;

public sealed class Result
{
    private readonly Error? _error;

    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(true, null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Type == ErrorType.None)
        {
            throw new ArgumentException("A failure requires a non-None error.", nameof(error));
        }

        return new Result(false, error);
    }
}

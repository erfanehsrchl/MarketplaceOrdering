namespace MarketplaceOrdering.Domain.Shared;

public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public Error Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A successful result has no error.");

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(true, value, null);
    }

    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Type == ErrorType.None)
        {
            throw new ArgumentException("A failure requires a non-None error.", nameof(error));
        }

        return new Result<T>(false, default, error);
    }
}

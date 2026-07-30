using System.Collections.ObjectModel;
using MarketplaceOrdering.Domain.Shared;

namespace MarketplaceOrdering.Api.ErrorHandling;

public sealed record ApiErrorResponse
{
    private readonly IReadOnlyDictionary<string, object?> _metadata;

    public ApiErrorResponse(
        string code,
        string message,
        string type,
        IReadOnlyDictionary<string, object?> metadata)
    {
        Code = code;
        Message = message;
        Type = type;
        _metadata = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(metadata, StringComparer.Ordinal));
    }

    public string Code { get; }
    public string Message { get; }
    public string Type { get; }
    public IReadOnlyDictionary<string, object?> Metadata => _metadata;

    public static ApiErrorResponse From(Error error) =>
        new(
            error.Code,
            error.Message,
            error.Type.ToString(),
            error.Metadata.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.Ordinal));
}

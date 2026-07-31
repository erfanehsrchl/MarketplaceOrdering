using System.Collections.ObjectModel;

namespace MarketplaceOrdering.Domain.Shared;

public sealed record Error
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private Error(
        string code,
        string message,
        ErrorType type,
        IReadOnlyDictionary<string, string> metadata,
        bool validate)
    {
        if (validate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
        }

        Code = code;
        Message = message;
        Type = type;
        Metadata = metadata;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorType Type { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static Error None { get; } =
        new(string.Empty, string.Empty, ErrorType.None, EmptyMetadata, false);

    public static Error Validation(
        string code,
        string message,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Create(code, message, ErrorType.Validation, metadata);

    public static Error NotFound(
        string code,
        string message,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Create(code, message, ErrorType.NotFound, metadata);

    public static Error BusinessRule(
        string code,
        string message,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Create(code, message, ErrorType.BusinessRule, metadata);

    public static Error Conflict(
        string code,
        string message,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Create(code, message, ErrorType.Conflict, metadata);

    public static Error Concurrency(
        string code,
        string message,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Create(code, message, ErrorType.Concurrency, metadata);

    public static Error CapacityExceeded(
        string code,
        string message,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Create(code, message, ErrorType.CapacityExceeded, metadata);

    public static Error DependencyFailure(
        string code,
        string message,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Create(code, message, ErrorType.DependencyFailure, metadata);

    private static Error Create(
        string code,
        string message,
        ErrorType type,
        IReadOnlyDictionary<string, string>? metadata)
    {
        var metadataCopy = metadata is null
            ? EmptyMetadata
            : new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(metadata, StringComparer.Ordinal));

        return new Error(code, message, type, metadataCopy, true);
    }
}

using System.Globalization;

namespace MarketplaceOrdering.Application.Checkout.CheckoutOrder;

/// <summary>
/// Builds the diagnostic metadata attached to Checkout errors.
/// </summary>
/// <remarks>
/// Every value is formatted with the invariant culture, because these strings
/// end up in logs and error payloads that are correlated across machines: a
/// number rendered differently per locale is not a usable correlation key.
/// </remarks>
public static class CheckoutMetadata
{
    public static IReadOnlyDictionary<string, string> Of(
        params (string Key, object Value)[] values) =>
        values.ToDictionary(
            value => value.Key,
            value => Convert.ToString(
                value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            StringComparer.Ordinal);
}

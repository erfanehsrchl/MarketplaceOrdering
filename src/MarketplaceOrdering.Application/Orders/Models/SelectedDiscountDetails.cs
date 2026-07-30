namespace MarketplaceOrdering.Application.Orders.Models;

public sealed record SelectedDiscountDetails(
    string Code,
    DateTimeOffset SelectedAt);

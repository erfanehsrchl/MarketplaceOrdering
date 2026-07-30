namespace MarketplaceOrdering.Application.Orders.Models;

public sealed record CancellationDetails(
    string Reason,
    DateTimeOffset CancelledAt,
    string PreviousStatus);

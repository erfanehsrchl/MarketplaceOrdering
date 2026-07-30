using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.TestFixtures;

internal static class OrderTestData
{
    internal static readonly DateTimeOffset CreatedAt =
        new(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);

    internal static OrderId OrderId() =>
        MarketplaceOrdering.Domain.ValueObjects.OrderId.New();

    internal static CustomerId CustomerId() =>
        MarketplaceOrdering.Domain.ValueObjects.CustomerId.Create(Guid.NewGuid()).Value;

    internal static DeliveryAddress Address() =>
        DeliveryAddress.Create("10 Main Street").Value;

    internal static ProductReference Product(int number, string? name = null) =>
        new(
            ProductId.Create(Guid.Parse($"{number:D8}-0000-0000-0000-000000000000")).Value,
            ProductName.Create(name ?? $"Product {number}").Value);

    internal static Quantity Quantity(int value) =>
        MarketplaceOrdering.Domain.ValueObjects.Quantity.Create(value).Value;

    internal static InitialOrderItem Initial(
        int productNumber,
        int quantity = 1,
        string? name = null) =>
        new(Product(productNumber, name), Quantity(quantity));

    internal static Order CreateOrder(params InitialOrderItem[] items) =>
        Order.Create(
            OrderId(),
            CustomerId(),
            Address(),
            items.Length == 0 ? [Initial(1)] : items,
            CreatedAt).Value;
}

using MarketplaceOrdering.Application.Common.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal static class ApplicationTestData
{
    internal static Order CreateOrder(int itemCount = 1)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(number => new InitialOrderItem(
                Product(number),
                Quantity.Create(number).Value))
            .ToArray();
        return Order.Create(
            OrderId.New(),
            CustomerId.Create(Guid.Parse(
                "10000000-0000-0000-0000-000000000000")).Value,
            DeliveryAddress.Create("10 Main Street").Value,
            items,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Value;
    }

    internal static ProductReference Product(int number) => new(
        ProductId.Create(Guid.Parse(
            $"{number:D8}-0000-0000-0000-000000000000")).Value,
        ProductName.Create($"Product {number}").Value);

    internal static VersionedOrder Versioned(Order order, long version = 4) =>
        new(order, version);
}

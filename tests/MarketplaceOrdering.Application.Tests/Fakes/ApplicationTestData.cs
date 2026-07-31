using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Fakes;

internal static class ApplicationTestData
{
    internal static DiscountPolicy DiscountPolicy(
        string code = "SAVE",
        bool isActive = true,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null) =>
        Domain.Discounts.DiscountPolicy.Create(
            DiscountCode.Create(code).Value,
            PercentageDiscountValue.Create(10).Value,
            isActive,
            startsAt,
            endsAt).Value;

    internal static FakeDiscountPolicyProvider DiscountProvider(
        DiscountPolicy? policy = null) =>
        new() { Policy = policy ?? DiscountPolicy() };

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

    internal static Order Persisted(Order order, long version = 4)
    {
        order.UpdatePersistenceVersion(version);
        order.ClearCommittedDomainEvents();
        return order;
    }
}

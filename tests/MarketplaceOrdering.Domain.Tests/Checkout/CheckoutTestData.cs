using FluentAssertions;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Tests.TestFixtures;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Tests.Checkout;

internal static class CheckoutTestData
{
    internal static readonly DateTimeOffset StartedAt =
        new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    internal static VendorId Vendor(int number) =>
        VendorId.Create(Guid.Parse(
            $"{number:D8}-2222-0000-0000-000000000000")).Value;

    internal static (
        Order Order,
        CheckoutAttemptId AttemptId,
        FulfillmentPlan Plan,
        VendorId[] Vendors) StartedWithPlan(int vendorCount = 1)
    {
        var order = OrderTestData.CreateOrder(
            OrderTestData.Initial(1, vendorCount));
        var attemptId = CheckoutAttemptId.New();
        var vendors = Enumerable.Range(1, vendorCount)
            .Select(Vendor)
            .ToArray();
        var productId = order.Items.Single().ProductId;
        var offers = vendors.Select(vendor =>
            ProductOffer.Create(
                vendor,
                productId,
                MoneyValue.Create(100).Value,
                1,
                MoneyValue.Zero,
                MoneyValue.Zero,
                24).Value).ToArray();
        var planner = new FulfillmentPlanner(
            new ProportionalDiscountAllocator());
        var plan = planner.CreateBestPlan(
            order.GetDemandSnapshot(),
            offers,
            null,
            StartedAt).Value;

        order.StartCheckout(attemptId, StartedAt).IsSuccess.Should().BeTrue();
        order.AttachFulfillmentPlan(
            attemptId,
            plan,
            StartedAt.AddMinutes(1)).IsSuccess.Should().BeTrue();

        return (order, attemptId, plan, vendors);
    }

    internal static FulfillmentPlan PlanFor(Order order)
    {
        var vendor = Vendor(50);
        var offers = order.GetDemandSnapshot()
            .Select(demand => ProductOffer.Create(
                vendor,
                demand.Product.ProductId,
                MoneyValue.Create(100).Value,
                demand.Quantity.Value,
                MoneyValue.Zero,
                MoneyValue.Zero,
                24).Value)
            .ToArray();

        return new FulfillmentPlanner(new ProportionalDiscountAllocator())
            .CreateBestPlan(
                order.GetDemandSnapshot(),
                offers,
                null,
                StartedAt).Value;
    }

    internal static ReservationOperationKey Begin(
        Order order,
        CheckoutAttemptId attemptId,
        VendorId vendor,
        int minute = 2)
    {
        var key = ReservationOperationKey.For(order.Id, attemptId, vendor);
        order.BeginInventoryReservation(
            attemptId,
            vendor,
            key,
            StartedAt.AddMinutes(minute)).IsSuccess.Should().BeTrue();
        return key;
    }
}

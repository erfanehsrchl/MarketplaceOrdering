using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Orders.CreateOrder;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Application.Tests.Checkout;

internal sealed class CheckoutTestContext
{
    internal required Order Order { get; init; }
    internal required CheckoutOrderCommandHandler Handler { get; init; }
    internal required FakeOrderRepository Repository { get; init; }
    internal required FakeProductOfferProvider Offers { get; init; }
    internal required FakeDiscountPolicyProvider Discounts { get; init; }
    internal required FakeInventoryReservationService Inventory { get; init; }
    internal required FakeCheckoutIdempotencyStore Idempotency { get; init; }
    internal required FakeReservationRecoveryStore Recovery { get; init; }
    internal required FakeClock Clock { get; init; }
    internal required IReservationReleaseCoordinator Coordinator { get; init; }
    internal required ICheckoutCompensationCoordinator Compensation { get; init; }
    internal required List<string> Journal { get; init; }
}

internal static class CheckoutHandlerTestData
{
    internal static CheckoutTestContext Create(int vendorCount = 1)
    {
        var order = ApplicationTestData.CreateOrder(
            vendorCount == 3 ? 2 : 1);
        if (vendorCount == 3)
        {
            var items = order.Items.ToArray();
            order.ChangeItemQuantity(
                items[0].ProductId,
                Quantity.Create(2).Value,
                order.CreatedAt);
            order.ChangeItemQuantity(
                items[1].ProductId,
                Quantity.Create(1).Value,
                order.CreatedAt);
        }
        else if (vendorCount > 1)
        {
            order.ChangeItemQuantity(
                order.Items.Single().ProductId,
                Quantity.Create(vendorCount).Value,
                order.CreatedAt);
        }
        var journal = new List<string>();
        var repository = new FakeOrderRepository
        {
            LoadedOrder = ApplicationTestData.Persisted(order, 4),
            Journal = journal
        };
        var offers = new FakeProductOfferProvider { Journal = journal };
        offers.Offers = vendorCount == 3
            ?
            [
                Offer(Vendor(1), order.Items.ElementAt(0).ProductId, 101),
                Offer(Vendor(2), order.Items.ElementAt(0).ProductId, 102),
                Offer(Vendor(3), order.Items.ElementAt(1).ProductId, 103)
            ]
            : Enumerable.Range(1, vendorCount)
                .Select(number => Offer(
                    Vendor(number),
                    order.Items.Single().ProductId,
                    100 + number))
                .ToArray();
        var discounts = new FakeDiscountPolicyProvider { Journal = journal };
        var inventory = new FakeInventoryReservationService { Journal = journal };
        var idempotency = new FakeCheckoutIdempotencyStore { Journal = journal };
        var recovery = new FakeReservationRecoveryStore();
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(
                2026, 7, 1, 12, 10, 0, TimeSpan.Zero)
        };
        var planner = new FulfillmentPlanner(
            new ProportionalDiscountAllocator());
        var coordinator = new ReservationReleaseCoordinator(
            inventory, repository, clock);
        var guard = new CheckoutIdempotencyGuard(idempotency, clock);
        var compensation = new CheckoutCompensationCoordinator(
            repository, inventory, recovery, coordinator, clock);
        var useCase = new CheckoutOrderCommandHandler(
            repository,
            offers,
            discounts,
            inventory,
            guard,
            compensation,
            clock,
            planner);

        return new CheckoutTestContext
        {
            Order = order,
            Handler = useCase,
            Repository = repository,
            Offers = offers,
            Discounts = discounts,
            Inventory = inventory,
            Idempotency = idempotency,
            Recovery = recovery,
            Clock = clock,
            Coordinator = coordinator,
            Compensation = compensation,
            Journal = journal
        };
    }

    internal static CheckoutOrderCommand Command(Order order, string key = "key") =>
        new(order.Id.Value, key);

    internal static VendorId Vendor(int number) =>
        VendorId.Create(Guid.Parse(
            $"{number:D8}-7000-0000-0000-000000000000")).Value;

    private static ProductOffer Offer(
        VendorId vendorId,
        ProductId productId,
        long price) =>
        ProductOffer.Create(
            vendorId,
            productId,
            MoneyValue.Create(price).Value,
            1,
            MoneyValue.Zero,
            MoneyValue.Zero,
            24).Value;
}

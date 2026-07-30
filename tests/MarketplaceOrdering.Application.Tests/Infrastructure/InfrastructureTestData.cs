using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Money;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Infrastructure;

internal static class InfrastructureTestData
{
    internal static readonly DateTimeOffset Now =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    internal static Order Order(OrderId? id = null)
    {
        var product = Product(1);
        return Domain.Orders.Order.Create(
            id ?? OrderId.New(),
            CustomerId.Create(Guid.Parse(
                "10000000-0000-0000-0000-000000000000")).Value,
            DeliveryAddress.Create("10 Main Street").Value,
            [new InitialOrderItem(
                new ProductReference(product, ProductName.Create("One").Value),
                Quantity.Create(1).Value)],
            Now).Value;
    }

    internal static ProductId Product(int number) => ProductId.Create(
        Guid.Parse($"{number:D8}-0000-0000-0000-000000000000")).Value;

    internal static VendorId Vendor(int number) => VendorId.Create(
        Guid.Parse($"{number:D8}-7000-0000-0000-000000000000")).Value;

    internal static ProductOffer Offer(
        int product = 1,
        int vendor = 1,
        long price = 100) =>
        ProductOffer.Create(
            Vendor(vendor), Product(product), Money.Create(price).Value,
            10, Money.Zero, Money.Zero, 24).Value;

    internal static DiscountPolicy Policy(string code, long amount) =>
        DiscountPolicy.Create(
            DiscountCode.Create(code).Value,
            FixedDiscountValue.Create(Money.Create(amount).Value).Value,
            true).Value;

    internal static InventoryReservationRequest ReservationRequest(
        ReservationOperationKey? key = null,
        int quantity = 2,
        int product = 1,
        int vendor = 1,
        OrderId? orderId = null,
        CheckoutAttemptId? attemptId = null) =>
        new(
            orderId ?? OrderId.New(),
            attemptId ?? CheckoutAttemptId.New(),
            Vendor(vendor),
            key ?? ReservationOperationKey.Create(Guid.NewGuid().ToString()).Value,
            [new InventoryReservationItem(
                Product(product), Quantity.Create(quantity).Value)]);

    internal static CheckoutOperationResult CheckoutResult(
        OrderId orderId,
        CheckoutAttemptId attemptId,
        long version = 2) =>
        new(orderId, attemptId, OrderStatus.AwaitingPayment,
            Money.Create(100).Value, Now.AddMinutes(15), version);

    internal static ReservationRecoveryRecord RecoveryRecord(
        ReservationOperationKey key,
        int attempts = 1,
        DateTimeOffset? createdAt = null) =>
        new(
            OrderId.New(), CheckoutAttemptId.New(), Vendor(1), key,
            ReservationId.New(), "release.failed",
            createdAt ?? Now, attempts);

    internal static Order MakePaid(
        Order order,
        TransactionId transactionId)
    {
        var plan = MakeAwaitingPayment(order);
        order.ConfirmPayment(
            transactionId, plan.TotalPayable, Now.AddMinutes(4));
        return order;
    }

    internal static FulfillmentPlan MakeAwaitingPayment(Order order)
    {
        var attemptId = CheckoutAttemptId.New();
        var vendorId = Vendor(1);
        var plan = new FulfillmentPlanner(
            new ProportionalDiscountAllocator()).CreateBestPlan(
                order.GetDemandSnapshot(),
                [Offer()],
                null,
                Now).Value;
        order.StartCheckout(attemptId, Now.AddMinutes(1));
        order.AttachFulfillmentPlan(attemptId, plan, Now.AddMinutes(1));
        var operationKey = ReservationOperationKey.For(
            order.Id, attemptId, vendorId);
        order.BeginInventoryReservation(
            attemptId, vendorId, operationKey, Now.AddMinutes(1));
        order.RecordInventoryReservationSucceeded(
            attemptId, operationKey, ReservationId.New(), Now.AddMinutes(2));
        order.CompleteCheckout(attemptId, Now.AddMinutes(3));
        return plan;
    }
}

internal sealed class InfrastructureTestClock(
    DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}

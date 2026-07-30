using FluentAssertions;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Common.Models;
using MarketplaceOrdering.Application.Tests.Checkout;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Domain.Money;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Payments;

public sealed class ConfirmPaymentConcurrencyTests
{
    [Fact]
    public async Task PaymentWinner_ShouldMakeStaleExpirationConflict()
    {
        var orderId = OrderId.New();
        var paymentSnapshot = AwaitingPayment(orderId);
        var expirationSnapshot = AwaitingPayment(orderId);
        var transactionId = TransactionId.Create("payment-wins").Value;
        paymentSnapshot.ConfirmPayment(
            transactionId,
            paymentSnapshot.CheckoutAttempt!.FulfillmentPlan!.TotalPayable,
            paymentSnapshot.PaymentExpiresAt!.Value.AddSeconds(-1));
        expirationSnapshot.Expire(
            expirationSnapshot.PaymentExpiresAt!.Value);
        var repository = Repository(paymentSnapshot, 20);

        var paymentSave = await repository.SavePaymentAsync(
            paymentSnapshot, 20, transactionId, CancellationToken.None);
        var expirationSave = await repository.SaveAsync(
            expirationSnapshot, 20, CancellationToken.None);

        paymentSave.Value.Should().Be(21);
        expirationSave.Error.Should().Be(
            ApplicationErrors.OrderVersionConflict);
        repository.LoadedOrder!.Order.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task ExpirationWinner_ShouldMakeStalePaymentConflict()
    {
        var orderId = OrderId.New();
        var paymentSnapshot = AwaitingPayment(orderId);
        var expirationSnapshot = AwaitingPayment(orderId);
        var transactionId = TransactionId.Create("expiration-wins").Value;
        paymentSnapshot.ConfirmPayment(
            transactionId,
            paymentSnapshot.CheckoutAttempt!.FulfillmentPlan!.TotalPayable,
            paymentSnapshot.PaymentExpiresAt!.Value.AddSeconds(-1));
        expirationSnapshot.Expire(
            expirationSnapshot.PaymentExpiresAt!.Value);
        var repository = Repository(expirationSnapshot, 20);

        var expirationSave = await repository.SaveAsync(
            expirationSnapshot, 20, CancellationToken.None);
        var paymentSave = await repository.SavePaymentAsync(
            paymentSnapshot, 20, transactionId, CancellationToken.None);

        expirationSave.Value.Should().Be(21);
        paymentSave.Error.Should().Be(
            ApplicationErrors.OrderVersionConflict);
        repository.LoadedOrder!.Order.Status.Should().Be(OrderStatus.Expired);
    }

    private static FakeOrderRepository Repository(Order order, long version) =>
        new()
        {
            LoadedOrder = new VersionedOrder(order, version),
            EnforceVersionChecks = true
        };

    private static Order AwaitingPayment(OrderId orderId)
    {
        var product = new ProductReference(
            ProductId.Create(Guid.Parse(
                "70000000-0000-0000-0000-000000000000")).Value,
            ProductName.Create("Product").Value);
        var order = Order.Create(
            orderId,
            CustomerId.Create(Guid.Parse(
                "80000000-0000-0000-0000-000000000000")).Value,
            DeliveryAddress.Create("Address").Value,
            [new InitialOrderItem(product, Quantity.Create(1).Value)],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Value;
        var vendor = CheckoutUseCaseTestData.Vendor(1);
        var offer = ProductOffer.Create(
            vendor,
            product.ProductId,
            Money.Create(100).Value,
            1,
            Money.Zero,
            Money.Zero,
            24).Value;
        var attemptId = CheckoutAttemptId.New();
        var at = new DateTimeOffset(
            2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
        order.StartCheckout(attemptId, at);
        var plan = new FulfillmentPlanner(
            new ProportionalDiscountAllocator()).CreateBestPlan(
                order.GetDemandSnapshot(), [offer], null, at).Value;
        order.AttachFulfillmentPlan(attemptId, plan, at);
        var key = ReservationOperationKey.For(order.Id, attemptId, vendor);
        order.BeginInventoryReservation(attemptId, vendor, key, at);
        order.RecordInventoryReservationSucceeded(
            attemptId, key, ReservationId.New(), at);
        order.CompleteCheckout(attemptId, at.AddMinutes(1));
        return order;
    }
}

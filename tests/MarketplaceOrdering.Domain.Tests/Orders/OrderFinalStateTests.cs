using FluentAssertions;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Tests.Checkout;
using MarketplaceOrdering.Domain.Tests.Payments;
using MarketplaceOrdering.Domain.Tests.TestFixtures;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Orders;

public sealed class OrderFinalStateTests
{
    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.AwaitingPayment)]
    public void AllowedStatus_ShouldCancelIdempotently(OrderStatus source)
    {
        var order = CreateInStatus(source);
        var reason = CancellationReason.Create("Customer request").Value;
        var cancelledAt = CheckoutTestData.StartedAt.AddHours(1);
        order.ClearCommittedDomainEvents();

        order.Cancel(reason, cancelledAt).IsSuccess.Should().BeTrue();
        order.Cancel(
            CancellationReason.Create("Replacement").Value,
            cancelledAt.AddHours(1)).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.Cancellation!.Reason.Should().Be(reason);
        order.Cancellation.CancelledAt.Should().Be(cancelledAt);
        order.Cancellation.PreviousStatus.Should().Be(source);
        order.DomainEvents.OfType<OrderCancelledDomainEvent>()
            .Should().ContainSingle();
    }

    [Fact]
    public void PaidAndExpiredOrders_ShouldNotCancel()
    {
        var paid = OrderPaymentTests.AwaitingPaymentOrder(out var expiresAt);
        paid.ConfirmPayment(
            TransactionId.Create("paid").Value,
            paid.CheckoutAttempt!.FulfillmentPlan!.TotalPayable,
            expiresAt.AddSeconds(-1));
        var expired = OrderPaymentTests.AwaitingPaymentOrder(
            out var expiration);
        expired.Expire(expiration);
        var reason = CancellationReason.Create("reason").Value;

        paid.Cancel(reason, expiresAt).Error.Should()
            .Be(CancellationErrors.NotAllowed);
        expired.Cancel(reason, expiration).Error.Should()
            .Be(CancellationErrors.NotAllowed);
    }

    [Fact]
    public void AwaitingPayment_ShouldExpireAtBoundaryIdempotently()
    {
        var order = OrderPaymentTests.AwaitingPaymentOrder(
            out var paymentExpiresAt);
        order.ClearCommittedDomainEvents();

        order.Expire(paymentExpiresAt).IsSuccess.Should().BeTrue();
        order.Expire(paymentExpiresAt.AddHours(1)).IsSuccess.Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Expired);
        order.ExpiredAt.Should().Be(paymentExpiresAt);
        order.DomainEvents.OfType<OrderExpiredDomainEvent>()
            .Should().ContainSingle()
            .Which.PaymentExpiresAt.Should().Be(paymentExpiresAt);
    }

    [Fact]
    public void EarlyExpiration_ShouldNotMutateOrRaiseEvent()
    {
        var order = OrderPaymentTests.AwaitingPaymentOrder(
            out var paymentExpiresAt);
        order.ClearCommittedDomainEvents();

        order.Expire(paymentExpiresAt.AddTicks(-1)).Error.Should()
            .Be(ExpirationErrors.NotDue);

        order.Status.Should().Be(OrderStatus.AwaitingPayment);
        order.ExpiredAt.Should().BeNull();
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void TechnicalCleanup_ShouldRemainAllowedInTerminalStates()
    {
        var cancelled = OrderPaymentTests.AwaitingPaymentOrder(out _);
        var cancelledReservation =
            cancelled.CheckoutAttempt!.Reservations.Single();
        cancelled.Cancel(
            CancellationReason.Create("reason").Value,
            CheckoutTestData.StartedAt);
        cancelled.MarkInventoryReservationReleasePending(
            cancelled.CheckoutAttempt.Id,
            cancelledReservation.ReservationId!.Value,
            "release.failed",
            CheckoutTestData.StartedAt).IsSuccess.Should().BeTrue();
        cancelled.Status.Should().Be(OrderStatus.Cancelled);

        var expired = OrderPaymentTests.AwaitingPaymentOrder(
            out var expiresAt);
        var expiredReservation =
            expired.CheckoutAttempt!.Reservations.Single();
        expired.Expire(expiresAt);
        expired.MarkInventoryReservationReleased(
            expired.CheckoutAttempt.Id,
            expiredReservation.ReservationId!.Value,
            expiresAt).IsSuccess.Should().BeTrue();
        expired.Status.Should().Be(OrderStatus.Expired);
    }

    private static Order CreateInStatus(OrderStatus status)
    {
        if (status == OrderStatus.Draft)
            return OrderTestData.CreateOrder();
        if (status == OrderStatus.Processing)
        {
            var order = OrderTestData.CreateOrder();
            order.StartCheckout(
                CheckoutAttemptId.New(), CheckoutTestData.StartedAt);
            return order;
        }
        return OrderPaymentTests.AwaitingPaymentOrder(out _);
    }
}

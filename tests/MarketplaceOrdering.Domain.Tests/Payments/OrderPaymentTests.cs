using FluentAssertions;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Payments;
using MarketplaceOrdering.Domain.Tests.Checkout;
using MarketplaceOrdering.Domain.ValueObjects;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Tests.Payments;

public sealed class OrderPaymentTests
{
    [Fact]
    public void AwaitingPayment_ShouldConfirmExactPaymentOnce()
    {
        var order = AwaitingPaymentOrder(out var expiresAt);
        var transactionId = TransactionId.Create("transaction-1").Value;
        var amount = order.CheckoutAttempt!.FulfillmentPlan!.TotalPayable;
        var paidAt = expiresAt.AddSeconds(-1);
        order.ClearCommittedDomainEvents();

        order.ConfirmPayment(
            transactionId, amount, paidAt, paidAt).IsSuccess.Should().BeTrue();
        order.ConfirmPayment(
            transactionId, amount, paidAt.AddMinutes(1), paidAt.AddMinutes(1))
            .IsSuccess.Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Paid);
        order.Payment.Should().NotBeNull();
        order.Payment!.PaidAt.Should().Be(paidAt);
        order.DomainEvents.OfType<OrderPaidDomainEvent>()
            .Should().ContainSingle()
            .Which.OccurredAt.Should().Be(paidAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void WrongAmount_ShouldFail(long difference)
    {
        var order = AwaitingPaymentOrder(out var expiresAt);
        var expected = order.CheckoutAttempt!.FulfillmentPlan!
            .TotalPayable.Amount;
        var at = expiresAt.AddSeconds(-1);

        var result = order.ConfirmPayment(
            TransactionId.Create("transaction").Value,
            MoneyValue.Create(expected + difference).Value,
            at,
            at);

        result.Error.Should().Be(PaymentErrors.AmountMismatch);
        order.Status.Should().Be(OrderStatus.AwaitingPayment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PaymentAtOrAfterExpiration_ShouldFail(int secondsAfter)
    {
        var order = AwaitingPaymentOrder(out var expiresAt);
        var at = expiresAt.AddSeconds(secondsAfter);

        var result = order.ConfirmPayment(
            TransactionId.Create("transaction").Value,
            order.CheckoutAttempt!.FulfillmentPlan!.TotalPayable,
            at,
            at);

        result.Error.Should().Be(PaymentErrors.ReservationExpired);
    }

    /// <summary>
    /// The reported payment time is provider input. If it were trusted for the
    /// expiration decision, backdating it would buy stock the Reservation no
    /// longer holds.
    /// </summary>
    [Fact]
    public void BackdatedPaidAt_ShouldNotRevivePaymentAfterExpiration()
    {
        var order = AwaitingPaymentOrder(out var expiresAt);

        var result = order.ConfirmPayment(
            TransactionId.Create("transaction").Value,
            order.CheckoutAttempt!.FulfillmentPlan!.TotalPayable,
            expiresAt.AddMinutes(-5),
            expiresAt.AddSeconds(1));

        result.Error.Should().Be(PaymentErrors.ReservationExpired);
        order.Status.Should().Be(OrderStatus.AwaitingPayment);
        order.Payment.Should().BeNull();
    }

    [Fact]
    public void ReportedTimeFarFromMarketplaceClock_ShouldFail()
    {
        var order = AwaitingPaymentOrder(out var expiresAt);
        var amount = order.CheckoutAttempt!.FulfillmentPlan!.TotalPayable;
        var confirmedAt = expiresAt.AddSeconds(-1);

        order.ConfirmPayment(
            TransactionId.Create("future").Value,
            amount,
            confirmedAt + PaymentPolicy.MaximumFutureSkew.Add(
                TimeSpan.FromSeconds(1)),
            confirmedAt).Error.Should()
            .Be(PaymentErrors.ReportedTimeNotAcceptable);

        order.ConfirmPayment(
            TransactionId.Create("stale").Value,
            amount,
            confirmedAt - PaymentPolicy.MaximumReportingDelay.Add(
                TimeSpan.FromSeconds(1)),
            confirmedAt).Error.Should()
            .Be(PaymentErrors.ReportedTimeNotAcceptable);

        order.Status.Should().Be(OrderStatus.AwaitingPayment);
    }

    [Fact]
    public void ReportedTimeInsideAcceptedWindow_ShouldSucceed()
    {
        var order = AwaitingPaymentOrder(out var expiresAt);
        // Far enough inside the window that the tolerated skew cannot push the
        // effective time past expiration; this asserts the skew rule alone.
        var confirmedAt = expiresAt
            - PaymentPolicy.MaximumFutureSkew
            - TimeSpan.FromSeconds(1);

        var result = order.ConfirmPayment(
            TransactionId.Create("slightly-ahead").Value,
            order.CheckoutAttempt!.FulfillmentPlan!.TotalPayable,
            confirmedAt + PaymentPolicy.MaximumFutureSkew,
            confirmedAt);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public void ConflictingPaymentReplay_ShouldFail()
    {
        var order = AwaitingPaymentOrder(out var expiresAt);
        var amount = order.CheckoutAttempt!.FulfillmentPlan!.TotalPayable;
        var at = expiresAt.AddSeconds(-1);
        order.ConfirmPayment(
            TransactionId.Create("first").Value,
            amount,
            at,
            at);

        order.ConfirmPayment(
            TransactionId.Create("second").Value,
            amount,
            expiresAt.AddSeconds(-2),
            expiresAt.AddSeconds(-2)).Error.Should()
            .Be(PaymentErrors.AlreadyConfirmedWithDifferentData);
        order.ConfirmPayment(
            TransactionId.Create("first").Value,
            MoneyValue.Create(amount.Amount + 1).Value,
            expiresAt.AddSeconds(-2),
            expiresAt.AddSeconds(-2)).Error.Should()
            .Be(PaymentErrors.AlreadyConfirmedWithDifferentData);
    }

    [Fact]
    public void PaymentRecord_ShouldBeImmutableAndRequirePositiveAmount()
    {
        PaymentRecord.Create(
            TransactionId.Create("transaction").Value,
            MoneyValue.Zero,
            CheckoutTestData.StartedAt).Error.Should()
            .Be(PaymentErrors.AmountNotPositive);
        typeof(PaymentRecord).GetProperties().Should().OnlyContain(
            property => property.SetMethod == null
                || !property.SetMethod.IsPublic);
    }

    internal static Order AwaitingPaymentOrder(
        out DateTimeOffset expiresAt,
        int vendorCount = 1)
    {
        var data = CheckoutTestData.StartedWithPlan(vendorCount);
        var index = 0;
        foreach (var vendor in data.Vendors)
        {
            var key = CheckoutTestData.Begin(
                data.Order, data.AttemptId, vendor, 2 + index);
            data.Order.RecordInventoryReservationSucceeded(
                data.AttemptId,
                key,
                ReservationId.New(),
                CheckoutTestData.StartedAt.AddMinutes(3 + index));
            index++;
        }
        data.Order.CompleteCheckout(
            data.AttemptId,
            CheckoutTestData.StartedAt.AddMinutes(5));
        expiresAt = data.Order.PaymentExpiresAt!.Value;
        return data.Order;
    }
}

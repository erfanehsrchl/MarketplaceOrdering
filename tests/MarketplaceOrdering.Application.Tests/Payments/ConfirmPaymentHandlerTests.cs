using FluentAssertions;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Common.Models;
using MarketplaceOrdering.Application.Payments.ConfirmPayment;
using MarketplaceOrdering.Application.Tests.Checkout;
using MarketplaceOrdering.Domain.Payments;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Payments;

public sealed class ConfirmPaymentCommandHandlerTests
{
    [Fact]
    public async Task ValidPayment_ShouldUseAtomicPaymentSave()
    {
        var context = await AwaitingContext();
        var transactionId = "transaction-1";
        var paidAt = context.Order.PaymentExpiresAt!.Value.AddSeconds(-1);
        using var cancellation = new CancellationTokenSource();

        var result = await new ConfirmPaymentCommandHandler(context.Repository)
            .Handle(
                new ConfirmPaymentCommand(
                    context.Order.Id.Value,
                    transactionId,
                    context.Order.CheckoutAttempt!.FulfillmentPlan!
                        .TotalPayable.Amount,
                    paidAt),
                cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Paid");
        result.Value.TransactionId.Should().Be(transactionId);
        result.Value.PaidAt.Should().Be(paidAt);
        result.Value.Version.Should().Be(10);
        context.Repository.SavePaymentCalls.Should().Be(1);
        context.Repository.SaveCalls.Should().Be(5);
        context.Repository.CapturedExpectedVersion.Should().Be(9);
        context.Repository.CapturedTransactionId!.Value.Should()
            .Be(transactionId);
        context.Repository.SavePaymentCancellationToken.Should()
            .Be(cancellation.Token);
    }

    [Fact]
    public async Task DomainFailure_ShouldNotPersistPayment()
    {
        var context = await AwaitingContext();

        var result = await new ConfirmPaymentCommandHandler(context.Repository)
            .Handle(
                new ConfirmPaymentCommand(
                    context.Order.Id.Value,
                    "transaction",
                    context.Order.CheckoutAttempt!.FulfillmentPlan!
                        .TotalPayable.Amount + 1,
                    context.Order.PaymentExpiresAt!.Value.AddSeconds(-1)),
                CancellationToken.None);

        result.Error.Should().Be(PaymentErrors.AmountMismatch);
        context.Repository.SavePaymentCalls.Should().Be(0);
    }

    [Fact]
    public async Task TransactionConflict_ShouldBeReturnedWithoutRetry()
    {
        var context = await AwaitingContext();
        var transactionId = TransactionId.Create("already-used").Value;
        context.Repository.ClaimedTransactionIds[transactionId.Value] =
            OrderId.New();

        var result = await new ConfirmPaymentCommandHandler(context.Repository)
            .Handle(
                new ConfirmPaymentCommand(
                    context.Order.Id.Value,
                    transactionId.Value,
                    context.Order.CheckoutAttempt!.FulfillmentPlan!
                        .TotalPayable.Amount,
                    context.Order.PaymentExpiresAt!.Value.AddSeconds(-1)),
                CancellationToken.None);

        result.Error.Should().Be(PaymentErrors.TransactionIdAlreadyUsed);
        context.Repository.SavePaymentCalls.Should().Be(1);
        context.Order.DomainEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task VersionConflict_ShouldRemainPendingWithoutRetry()
    {
        var context = await AwaitingContext();
        context.Repository.SavePaymentFailure =
            ApplicationErrors.OrderVersionConflict;
        var eventCount = context.Order.DomainEvents.Count;

        var result = await new ConfirmPaymentCommandHandler(context.Repository)
            .Handle(
                new ConfirmPaymentCommand(
                    context.Order.Id.Value,
                    "transaction",
                    context.Order.CheckoutAttempt!.FulfillmentPlan!
                        .TotalPayable.Amount,
                    context.Order.PaymentExpiresAt!.Value.AddSeconds(-1)),
                CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.OrderVersionConflict);
        context.Repository.SavePaymentCalls.Should().Be(1);
        context.Order.DomainEvents.Count.Should().Be(eventCount + 1);
    }

    private static async Task<CheckoutTestContext> AwaitingContext()
    {
        var context = CheckoutHandlerTestData.Create();
        context.Repository.EnforceVersionChecks = true;
        var checkout = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);
        context.Repository.LoadedOrder = new VersionedOrder(
            context.Order, checkout.Value.Version);
        return context;
    }
}

using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;

namespace MarketplaceOrdering.Application.Tests.Checkout;

public sealed class CheckoutOrderWorkflowTests
{
    [Fact]
    public async Task SuccessfulCheckout_ShouldPersistEveryBoundaryInOrder()
    {
        var context = CheckoutHandlerTestData.Create(2);

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(OrderStatus.AwaitingPayment);
        result.Value.Version.Should().Be(11);
        result.Value.TotalPayable.Should().Be(
            context.Order.CheckoutAttempt!.FulfillmentPlan!.TotalPayable);
        result.Value.PaymentExpiresAt.Should().Be(
            context.Order.CheckoutAttempt.PaymentExpiresAt);
        context.Order.CheckoutAttempt.Reservations.Should()
            .OnlyContain(reservation =>
                reservation.Status == InventoryReservationStatus.Active);
        context.Inventory.ReservationRequests.Select(request => request.VendorId.Value)
            .Should().BeInAscendingOrder();
        context.Inventory.ReservationRequests.Should().HaveCount(2);
        context.Discounts.CallCount.Should().Be(0);
        context.Offers.CapturedDemands.Should()
            .BeEquivalentTo(context.Order.GetDemandSnapshot());
        context.Idempotency.CompletedResult.Should().Be(result.Value);

        context.Journal.First().Should().Be("Idempotency.TryBegin");
        context.Journal[1].Should().Be("Repository.Load");
        context.Journal.IndexOf("Offers.Get").Should().BeGreaterThan(
            context.Journal.IndexOf("Repository.Save.Planning"));
        context.Journal.IndexOf("Inventory.Reserve."
                + context.Inventory.ReservationRequests[0].VendorId)
            .Should().BeGreaterThan(
                context.Journal.IndexOf("Repository.Save.Intent."
                    + context.Inventory.ReservationRequests[0].VendorId));
        context.Journal.IndexOf("Inventory.Reserve."
                + context.Inventory.ReservationRequests[1].VendorId)
            .Should().BeGreaterThan(
                context.Journal.IndexOf("Repository.Save.Success."
                    + context.Inventory.ReservationRequests[0].VendorId));
        context.Journal.IndexOf("Idempotency.Complete").Should()
            .BeGreaterThan(
                context.Journal.IndexOf("Repository.Save.AwaitingPayment"));
    }

    [Fact]
    public async Task ReservationRequests_ShouldContainVendorAllocationsOnly()
    {
        var context = CheckoutHandlerTestData.Create(2);

        await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        context.Inventory.ReservationRequests.Should().OnlyContain(
            request => request.Items.Count == 1
                && request.Items.Single().Quantity.Value == 1);
        context.Inventory.ReservationRequests.Should().OnlyContain(
            request => request.OperationKey.Value.Contains(
                request.VendorId.Value.ToString("N"),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectedDiscount_ShouldBeLoadedDuringCheckout()
    {
        var context = CheckoutHandlerTestData.Create();
        var code = MarketplaceOrdering.Domain.ValueObjects.DiscountCode
            .Create("SAVE").Value;
        context.Order.SelectDiscountCode(code, context.Clock.UtcNow);
        context.Discounts.Policy =
            MarketplaceOrdering.Domain.Discounts.DiscountPolicy.Create(
                code,
                MarketplaceOrdering.Domain.Discounts.PercentageDiscountValue
                    .Create(10).Value,
                true,
                context.Clock.UtcNow.AddDays(-1),
                context.Clock.UtcNow.AddDays(1)).Value;

        var result = await context.Handler.Handle(
            CheckoutHandlerTestData.Command(context.Order),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Discounts.CallCount.Should().Be(1);
        context.Discounts.CapturedCode.Should().Be(code);
    }
}

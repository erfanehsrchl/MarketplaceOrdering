using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MarketplaceOrdering.Api.Contracts.Payments;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Api.Tests.Infrastructure;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Application.Payments.ConfirmPayment;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Infrastructure.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Api.Tests.Payments;

public sealed class PaymentsControllerTests
{
    [Fact]
    public async Task ExactPaymentSucceedsAndIsVisibleThroughGet()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);
        var checkedOut = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        var request = new ConfirmPaymentRequest(
            "transaction-one",
            checkedOut!.CheckoutAttempt!.TotalPayable!.Value,
            factory.Clock.UtcNow);

        var response = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/payments/confirm", request);
        var payment =
            await response.Content.ReadFromJsonAsync<ConfirmPaymentResult>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payment!.Status.Should().Be("Paid");
        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Payment!.TransactionId.Should().Be("transaction-one");
        loaded.Payment.Amount.Should().Be(635);

        var replay = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/payments/confirm", request);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<ConfirmPaymentResult>())!
            .PaidAt.Should().Be(payment.PaidAt);
    }

    [Theory]
    [InlineData(634)]
    [InlineData(636)]
    public async Task InexactPaymentReturns422(long amount)
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);

        var response = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/payments/confirm",
            new ConfirmPaymentRequest(
                "bad-amount", amount,
                factory.Clock.UtcNow));

        response.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("payment.amount_mismatch");
    }

    [Fact]
    public async Task PaymentAtExpirationBoundaryReturns422()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);
        var checkedOut = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        var expiresAt = checkedOut!.CheckoutAttempt!.PaymentExpiresAt!.Value;
        factory.Clock.UtcNow = expiresAt;

        var response = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/payments/confirm",
            new ConfirmPaymentRequest(
                "expired",
                checkedOut.CheckoutAttempt.TotalPayable!.Value,
                expiresAt));

        response.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("payment.reservation_expired");
    }

    /// <summary>
    /// A caller who backdates <c>PaidAt</c> into the live Reservation window
    /// must not be able to pay after the marketplace clock passed expiration.
    /// </summary>
    [Fact]
    public async Task BackdatedPaidAtAfterExpirationReturns422()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);
        var checkedOut = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        var expiresAt = checkedOut!.CheckoutAttempt!.PaymentExpiresAt!.Value;
        factory.Clock.UtcNow = expiresAt.AddSeconds(1);

        var response = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/payments/confirm",
            new ConfirmPaymentRequest(
                "backdated",
                checkedOut.CheckoutAttempt.TotalPayable!.Value,
                expiresAt.AddMinutes(-10)));

        response.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("payment.reservation_expired");
        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Status.Should().Be("AwaitingPayment");
        loaded.Payment.Should().BeNull();
    }

    [Fact]
    public async Task TransactionIdCannotBeUsedByAnotherOrder()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var first = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(
            client, first.OrderId, "first-checkout");
        var firstCheckedOut = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{first.OrderId}");
        await client.PostAsJsonAsync(
            $"/api/orders/{first.OrderId}/payments/confirm",
            new ConfirmPaymentRequest(
                "shared-transaction",
                firstCheckedOut!.CheckoutAttempt!.TotalPayable!.Value,
                factory.Clock.UtcNow));

        var inventory = factory.Services.GetRequiredService<
            InMemoryInventoryReservationService>();
        inventory.SetAvailableQuantity(
            DemoDataSeeder.Vendor3Id, DemoDataSeeder.ProductAId, 3);
        inventory.SetAvailableQuantity(
            DemoDataSeeder.Vendor3Id, DemoDataSeeder.ProductBId, 2);
        var second = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(
            client, second.OrderId, "second-checkout");
        var secondCheckedOut = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{second.OrderId}");

        var conflict = await client.PostAsJsonAsync(
            $"/api/orders/{second.OrderId}/payments/confirm",
            new ConfirmPaymentRequest(
                "shared-transaction",
                secondCheckedOut!.CheckoutAttempt!.TotalPayable!.Value,
                factory.Clock.UtcNow));

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be(
                "payment.transaction_id_already_used");
    }
}

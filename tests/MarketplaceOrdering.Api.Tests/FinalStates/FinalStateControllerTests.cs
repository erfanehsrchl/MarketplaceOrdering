using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MarketplaceOrdering.Api.Contracts.Orders;
using MarketplaceOrdering.Api.Contracts.Payments;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Api.Tests.Infrastructure;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Orders.CancelOrder;
using MarketplaceOrdering.Application.Orders.ExpireOrder;
using MarketplaceOrdering.Application.Orders.Models;

namespace MarketplaceOrdering.Api.Tests.FinalStates;

public sealed class FinalStateControllerTests
{
    [Fact]
    public async Task DraftCancellationIsIdempotentAndPreservesOriginalReason()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);

        var first = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/cancel",
            new CancelOrderRequest("first reason"));
        var firstResult =
            (await first.Content.ReadFromJsonAsync<CancelOrderResult>())!;
        var replay = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/cancel",
            new CancelOrderRequest("different reason"));
        var replayResult =
            (await replay.Content.ReadFromJsonAsync<CancelOrderResult>())!;

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResult.Reason.Should().Be("first reason");
        replayResult.CancelledAt.Should().Be(firstResult.CancelledAt);
        (await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}"))!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task ReleaseFailureDoesNotReverseCancelledStatus()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync(
            "/api/demo/scenarios/release-failure", null);
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);

        var response = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/cancel",
            new CancelOrderRequest("customer request"));
        var cancelled =
            (await response.Content.ReadFromJsonAsync<CancelOrderResult>())!;

        cancelled.Status.Should().Be("Cancelled");
        cancelled.HasPendingReservationReleases.Should().BeTrue();
        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Status.Should().Be("Cancelled");
        loaded.HasPendingReservationReleases.Should().BeTrue();
    }

    [Fact]
    public async Task ExpirationHonorsBoundaryAndIsIdempotent()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);
        var checkedOut = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");

        var early = await client.PostAsync(
            $"/api/orders/{order.OrderId}/expire", null);
        early.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity);
        (await early.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("expiration.not_due");

        factory.Clock.UtcNow =
            checkedOut!.CheckoutAttempt!.PaymentExpiresAt!.Value;
        var expiredResponse = await client.PostAsync(
            $"/api/orders/{order.OrderId}/expire", null);
        var expired = (await expiredResponse.Content
            .ReadFromJsonAsync<ExpireOrderResult>())!;
        expired.Status.Should().Be("Expired");
        var replay = await client.PostAsync(
            $"/api/orders/{order.OrderId}/expire", null);
        (await replay.Content.ReadFromJsonAsync<ExpireOrderResult>())!
            .ExpiredAt.Should().Be(expired.ExpiredAt);
        (await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}"))!.Status.Should().Be("Expired");
    }

    [Fact]
    public async Task PaidOrderCannotCancelOrExpire()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);
        var checkedOut = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/payments/confirm",
            new ConfirmPaymentRequest(
                "paid-final",
                checkedOut!.CheckoutAttempt!.TotalPayable!.Value,
                factory.Clock.UtcNow.AddMinutes(1)));

        (await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/cancel",
            new CancelOrderRequest("not allowed"))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsync(
            $"/api/orders/{order.OrderId}/expire", null)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}

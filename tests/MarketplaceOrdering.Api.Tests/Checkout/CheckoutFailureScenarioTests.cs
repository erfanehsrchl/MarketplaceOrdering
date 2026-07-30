using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Api.Tests.Infrastructure;
using MarketplaceOrdering.Application.Orders.Models;

namespace MarketplaceOrdering.Api.Tests.Checkout;

public sealed class CheckoutFailureScenarioTests
{
    [Fact]
    public async Task ReservationRejectionReturns503AndCompensatesToDraft()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync(
            "/api/demo/scenarios/reservation-rejection", null);
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);

        var response = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId);

        response.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("reservation.demo_rejection");
        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Status.Should().Be("Draft");
        loaded.Items.Should().BeEquivalentTo(order.Items);
    }

    [Fact]
    public async Task IndeterminateReservationRemainsProcessingAndReplayConflicts()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync(
            "/api/demo/scenarios/reservation-indeterminate", null);
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);

        var first = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId);
        first.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await first.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be(
                "checkout.reservation_outcome_indeterminate");

        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Status.Should().Be("Processing");
        var replay = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId);
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await replay.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("checkout.idempotency_in_progress");
    }
}

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MarketplaceOrdering.Api.Contracts.Demo;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Api.Tests.Infrastructure;
using MarketplaceOrdering.Application.Checkout.AbandonStuckCheckout;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Infrastructure.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Api.Tests.Checkout;

/// <summary>
/// End-to-end recovery of an Order whose Checkout claimed it and then lost
/// contact with the Inventory service.
/// </summary>
public sealed class StuckCheckoutRecoveryTests
{
    [Fact]
    public async Task IndeterminateReservationLeavesTheOrderClaimed()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync(
            "/api/demo/scenarios/reservation-indeterminate", null);
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);

        var checkout = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId);

        checkout.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable);
        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Status.Should().Be("Processing");

        // Editing is blocked while the claim stands, which is exactly why the
        // claim cannot be allowed to last forever.
        var edit = await client.DeleteAsync(
            $"/api/orders/{order.OrderId}/discount");
        edit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AbandonBeforeTheTimeoutIsRefused()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync(
            "/api/demo/scenarios/reservation-indeterminate", null);
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);

        var response = await client.PostAsync(
            $"/api/orders/{order.OrderId}/checkout/abandon", null);

        response.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("checkout.not_stuck");
    }

    [Fact]
    public async Task AbandonAfterTheTimeoutReturnsTheOrderToDraft()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync(
            "/api/demo/scenarios/reservation-indeterminate", null);
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);
        factory.Clock.UtcNow += OrderPolicy.CheckoutAttemptTimeout;

        var response = await client.PostAsync(
            $"/api/orders/{order.OrderId}/checkout/abandon", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content
            .ReadFromJsonAsync<AbandonStuckCheckoutResult>();
        result!.Status.Should().Be("Draft");
        result.ResolvedReservations.Should().Be(1);
        result.PendingReleases.Should().Be(0);

        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Status.Should().Be("Draft");
        loaded.HasPendingReservationReleases.Should().BeFalse();
    }

    /// <summary>
    /// Recovery is what makes the original idempotency key usable again: the
    /// entry was left <c>InProgress</c> and reconciles once the attempt failed.
    /// </summary>
    [Fact]
    public async Task RecoveredOrderCanBeCheckedOutSuccessfullyAgain()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync(
            "/api/demo/scenarios/reservation-indeterminate", null);
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId, "stuck-key");
        factory.Clock.UtcNow += OrderPolicy.CheckoutAttemptTimeout;
        await client.PostAsync(
            $"/api/orders/{order.OrderId}/checkout/abandon", null);

        // The original key now replays the recorded failure instead of hanging.
        var replay = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId, "stuck-key");
        replay.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // Nothing was ever reserved, so once the Inventory service is healthy
        // the same Order checks out normally under a new key.
        factory.Services.GetRequiredService<InMemoryInventoryReservationService>()
            .ConfigureReservationBehavior(
                DemoDataSeeder.Vendor3Id, InMemoryReservationBehavior.Normal);
        var retry = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId, "fresh-key");

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Status.Should().Be("AwaitingPayment");
    }

    /// <summary>
    /// The dangerous variant: the reservation really happened and stock is held,
    /// but the response was lost. Recovery must read the outcome back and
    /// release it, otherwise the stock is gone until its own expiry.
    /// </summary>
    [Fact]
    public async Task LostReservationResponseIsResolvedAndTheStockIsReleased()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync(
            "/api/demo/scenarios/reservation-lost-response", null);
        var inventory = factory.Services
            .GetRequiredService<InMemoryInventoryReservationService>();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);

        inventory.GetAvailableQuantity(
            DemoDataSeeder.Vendor3Id, DemoDataSeeder.ProductAId)
            .Should().Be(0);
        factory.Clock.UtcNow += OrderPolicy.CheckoutAttemptTimeout;

        var response = await client.PostAsync(
            $"/api/orders/{order.OrderId}/checkout/abandon", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content
            .ReadFromJsonAsync<AbandonStuckCheckoutResult>())!
            .Status.Should().Be("Draft");
        inventory.GetAvailableQuantity(
            DemoDataSeeder.Vendor3Id, DemoDataSeeder.ProductAId)
            .Should().Be(3);
        inventory.GetAvailableQuantity(
            DemoDataSeeder.Vendor3Id, DemoDataSeeder.ProductBId)
            .Should().Be(2);
    }

    /// <summary>
    /// Every committed Domain Event reaches the outbox, in commit order, so a
    /// broker could be attached without changing the Domain or the Handlers.
    /// </summary>
    [Fact]
    public async Task CommittedDomainEventsAreVisibleInTheOutbox()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync("/api/demo/reset", null);
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, order.OrderId);

        var outbox = await client.GetFromJsonAsync<DomainEventOutboxResponse>(
            $"/api/demo/outbox?orderId={order.OrderId}");

        outbox!.Entries.Select(entry => entry.EventType).Should()
            .ContainInOrder(
                "OrderCreatedDomainEvent",
                "OrderItemAddedDomainEvent",
                "OrderSubmittedForProcessingDomainEvent",
                "FulfillmentPlanCreatedDomainEvent",
                "InventoryReservationRequestedDomainEvent",
                "InventoryReservedDomainEvent",
                "OrderAwaitingPaymentDomainEvent");
        outbox.Entries.Should().OnlyContain(
            entry => entry.OrderId == order.OrderId);
        outbox.Entries.Select(entry => entry.Sequence).Should()
            .BeInAscendingOrder().And.OnlyHaveUniqueItems();
        outbox.Entries.Select(entry => entry.EventId).Should()
            .OnlyHaveUniqueItems();
        outbox.Entries.Select(entry => entry.OrderVersion).Should()
            .BeInAscendingOrder();
    }
}

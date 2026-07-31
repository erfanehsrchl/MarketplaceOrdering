using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Api.Contracts.Orders;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Api.Tests.Infrastructure;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Infrastructure.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Api.Tests.Checkout;

public sealed class CheckoutControllerTests
{
    [Fact]
    public async Task DefaultCheckoutSelectsSingleVendorTieWinnerAndReplays()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);

        var response = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId);
        var checkoutJson =
            await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        checkoutJson.GetProperty("status").GetInt32()
            .Should().Be((int)OrderStatus.AwaitingPayment);
        checkoutJson.GetProperty("totalPayable").GetProperty("amount")
            .GetInt64().Should().Be(635);
        var inventory = factory.Services.GetRequiredService<
            InMemoryInventoryReservationService>();
        inventory.GetAvailableQuantity(
            DemoDataSeeder.Vendor3Id,
            DemoDataSeeder.ProductAId).Should().Be(0);
        inventory.GetAvailableQuantity(
            DemoDataSeeder.Vendor1Id,
            DemoDataSeeder.ProductAId).Should().Be(3);

        var replay = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId);
        var replayJson =
            await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayJson.GetProperty("checkoutAttemptId")
            .GetProperty("value").GetGuid()
            .Should().Be(
                checkoutJson.GetProperty("checkoutAttemptId")
                    .GetProperty("value").GetGuid());
        inventory.GetAvailableQuantity(
            DemoDataSeeder.Vendor3Id,
            DemoDataSeeder.ProductAId).Should().Be(0);

        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.Status.Should().Be("AwaitingPayment");
        loaded.CheckoutAttempt!.TotalPayable.Should().Be(635);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task MissingOrBlankIdempotencyHeaderReturns400(string? key)
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/orders/{order.OrderId}/checkout");
        if (key is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("idempotency_key.empty");
    }

    [Fact]
    public async Task SameKeyForDifferentOrderConflicts()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var first = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        var second = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await ApiTestWorkflow.CheckoutAsync(client, first.OrderId, "same");

        var response = await ApiTestWorkflow.CheckoutAsync(
            client, second.OrderId, "same");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("checkout.idempotency_conflict");
    }

    [Fact]
    public async Task DiscountAffectsProductsButNotShipping()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        await client.PutAsJsonAsync(
            $"/api/orders/{order.OrderId}/discount",
            new ApplyDiscountCodeRequest("SAVE10"));

        var response = await ApiTestWorkflow.CheckoutAsync(
            client, order.OrderId);
        var checkout =
            await response.Content.ReadFromJsonAsync<JsonElement>();

        checkout.GetProperty("totalPayable").GetProperty("amount")
            .GetInt64().Should().Be(575);
    }

    /// <summary>
    /// A code that can never work is rejected while the Order is still a Draft,
    /// so the customer learns immediately instead of at Checkout.
    /// </summary>
    [Fact]
    public async Task InactiveDiscountIsRejectedWhenApplied()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/orders/{order.OrderId}/discount",
            new ApplyDiscountCodeRequest("INACTIVE"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("discount.inactive");
        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.SelectedDiscount.Should().BeNull();
    }

    [Fact]
    public async Task UnknownDiscountCodeIsRejectedWhenApplied()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/orders/{order.OrderId}/discount",
            new ApplyDiscountCodeRequest("NO-SUCH-CODE"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            $"/api/orders/{order.OrderId}");
        loaded!.SelectedDiscount.Should().BeNull();
    }
}

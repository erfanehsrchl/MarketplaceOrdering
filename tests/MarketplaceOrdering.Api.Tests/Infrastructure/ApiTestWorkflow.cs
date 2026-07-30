using System.Net.Http.Json;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Api.Contracts.Orders;
using MarketplaceOrdering.Application.Orders.Models;

namespace MarketplaceOrdering.Api.Tests.Infrastructure;

internal static class ApiTestWorkflow
{
    internal static async Task<OrderDetails> CreateDefaultOrderAsync(
        HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest(
                DemoDataSeeder.CustomerId.Value,
                "10 Main Street",
                [
                    new CreateOrderItemRequest(
                        DemoDataSeeder.ProductAId.Value, "Product A", 3),
                    new CreateOrderItemRequest(
                        DemoDataSeeder.ProductBId.Value, "Product B", 2)
                ]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDetails>())!;
    }

    internal static async Task<HttpResponseMessage> CheckoutAsync(
        HttpClient client,
        Guid orderId,
        string key = "checkout-key")
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/orders/{orderId}/checkout");
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
}

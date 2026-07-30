using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Api.Contracts.Orders;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Api.Tests.Infrastructure;
using MarketplaceOrdering.Application.Orders.Models;

namespace MarketplaceOrdering.Api.Tests.Orders;

public sealed class OrdersControllerTests
{
    [Fact]
    public async Task CreateAndGetReturnPersistedTransportModels()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var request = new CreateOrderRequest(
            DemoDataSeeder.CustomerId.Value,
            "10 Main Street",
            [
                new CreateOrderItemRequest(
                    DemoDataSeeder.ProductAId.Value, "Original", 1),
                new CreateOrderItemRequest(
                    DemoDataSeeder.ProductAId.Value, "Changed", 2),
                new CreateOrderItemRequest(
                    DemoDataSeeder.ProductBId.Value, "Second", 1)
            ]);

        var response = await client.PostAsJsonAsync("/api/orders", request);
        var created =
            await response.Content.ReadFromJsonAsync<OrderDetails>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.OrderId.Should().NotBeEmpty();
        created.Version.Should().Be(1);
        created.Items.Select(item => item.ProductId).Should().ContainInOrder(
            DemoDataSeeder.ProductAId.Value,
            DemoDataSeeder.ProductBId.Value);
        created.Items.First().Quantity.Should().Be(3);
        created.Items.First().ProductName.Should().Be("Original");
        response.Headers.Location!.AbsolutePath.Should()
            .Be($"/api/orders/{created.OrderId}");

        var loaded = await client.GetFromJsonAsync<OrderDetails>(
            response.Headers.Location);
        loaded.Should().BeEquivalentTo(created);
    }

    [Theory]
    [InlineData(0, "Product", "quantity.not_positive")]
    [InlineData(1, " ", "product_name.empty")]
    public async Task InvalidItemValuesReturnStableBadRequest(
        int quantity,
        string productName,
        string expectedCode)
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest(
                DemoDataSeeder.CustomerId.Value,
                "Address",
                [new CreateOrderItemRequest(
                    DemoDataSeeder.ProductAId.Value,
                    productName,
                    quantity)]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error =
            await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        error!.Code.Should().Be(expectedCode);
        error.Message.Should().NotBeNullOrWhiteSpace();
        error.Type.Should().Be("Validation");
        error.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task EmptyItemsAndMissingOrderMapTo400And404()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var empty = await client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest(
                DemoDataSeeder.CustomerId.Value, "Address", []));
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await empty.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("order.items_required");

        var missing = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("order.not_found");
    }

    [Fact]
    public async Task EditingFlowUsesDomainRulesAndPersistsEachMutation()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var order = await ApiTestWorkflow.CreateDefaultOrderAsync(client);
        var thirdProduct = Guid.Parse(
            "20000000-0000-0000-0000-000000000003");

        (await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/items",
            new AddOrderItemRequest(thirdProduct, "Third", 1)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var duplicate = await client.PostAsJsonAsync(
            $"/api/orders/{order.OrderId}/items",
            new AddOrderItemRequest(thirdProduct, "Ignored", 2));
        (await duplicate.Content.ReadFromJsonAsync<OrderDetails>())!
            .Items.Single(item => item.ProductId == thirdProduct)
            .Quantity.Should().Be(3);

        (await client.PutAsJsonAsync(
            $"/api/orders/{order.OrderId}/items/{thirdProduct}",
            new ChangeOrderItemQuantityRequest(4)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync(
            $"/api/orders/{order.OrderId}/items/{thirdProduct}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PutAsJsonAsync(
            $"/api/orders/{order.OrderId}/discount",
            new ApplyDiscountCodeRequest("SAVE10")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync(
            $"/api/orders/{order.OrderId}/discount"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task QuantityLimitAndRemovingFinalItemReturn422()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest(
                DemoDataSeeder.CustomerId.Value,
                "Address",
                [new CreateOrderItemRequest(
                    DemoDataSeeder.ProductAId.Value, "A", 1)]));
        var order =
            (await response.Content.ReadFromJsonAsync<OrderDetails>())!;

        var tooMany = await client.PutAsJsonAsync(
            $"/api/orders/{order.OrderId}/items/{DemoDataSeeder.ProductAId.Value}",
            new ChangeOrderItemQuantityRequest(11));
        tooMany.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity);
        (await tooMany.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("order.quantity_limit_exceeded");

        var remove = await client.DeleteAsync(
            $"/api/orders/{order.OrderId}/items/{DemoDataSeeder.ProductAId.Value}");
        remove.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await remove.Content.ReadFromJsonAsync<ApiErrorResponse>())!
            .Code.Should().Be("order.last_item_cannot_be_removed");
    }
}

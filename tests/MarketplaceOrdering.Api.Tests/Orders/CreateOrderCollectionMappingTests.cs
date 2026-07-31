using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MediatR;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Api.Contracts.Orders;
using MarketplaceOrdering.Application.Orders.CreateOrder;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Domain.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Api.Tests.Orders;

public sealed class CreateOrderCollectionMappingTests
{
    [Fact]
    public async Task Controller_ShouldMaterializeOrderedApplicationInputsOnce()
    {
        await using var factory = new CollectionMappingApiFactory();
        using var client = factory.CreateClient();
        IReadOnlyCollection<CreateOrderItemRequest> apiItems =
        [
            new(DemoDataSeeder.ProductAId.Value, "First", 1),
            new(DemoDataSeeder.ProductBId.Value, "Second", 2)
        ];

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest(
                DemoDataSeeder.CustomerId.Value,
                "Address",
                apiItems));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var command = factory.Probe.Command
            ?? throw new InvalidOperationException(
                "The CreateOrder command was not captured.");
        command.Items.Should().BeOfType<CreateOrderItemInput[]>();
        command.Items!.Select(item => item.ProductId).Should().Equal(
            apiItems.Select(item => item.ProductId));
        command.Items.Select(item => item.ProductName).Should()
            .Equal("First", "Second");
        command.Items.Should().OnlyContain(item =>
            item.GetType() == typeof(CreateOrderItemInput));
    }

    private sealed class CollectionMappingApiFactory
        : WebApplicationFactory<Program>
    {
        internal CreateOrderCaptureProbe Probe { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(Probe);
                services.AddTransient<
                    IPipelineBehavior<CreateOrderCommand, Result<OrderDetails>>,
                    CreateOrderCaptureBehavior>();
            });
        }
    }

    private sealed class CreateOrderCaptureProbe
    {
        internal CreateOrderCommand? Command { get; set; }
    }

    private sealed class CreateOrderCaptureBehavior(
        CreateOrderCaptureProbe probe)
        : IPipelineBehavior<CreateOrderCommand, Result<OrderDetails>>
    {
        public async Task<Result<OrderDetails>> Handle(
            CreateOrderCommand request,
            RequestHandlerDelegate<Result<OrderDetails>> next,
            CancellationToken cancellationToken)
        {
            probe.Command = request;
            return await next();
        }
    }
}

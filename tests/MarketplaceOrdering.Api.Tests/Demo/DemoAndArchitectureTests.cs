using MediatR;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using MarketplaceOrdering.Api.Configuration;
using MarketplaceOrdering.Api.Contracts.Demo;
using MarketplaceOrdering.Api.Contracts.Orders;
using MarketplaceOrdering.Api.Controllers;
using MarketplaceOrdering.Api.ErrorHandling;
using MarketplaceOrdering.Api.Tests.Infrastructure;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Infrastructure.Inventory;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Api.Tests.Demo;

public sealed class DemoAndArchitectureTests
{
    [Fact]
    public async Task DemoResetScenariosRecoveryAndSwaggerAreAvailableInDevelopment()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();

        var reset = await client.PostAsync("/api/demo/reset", null);
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var demo =
            await reset.Content.ReadFromJsonAsync<DemoScenarioResponse>();
        demo!.Scenario.Should().Be("default");
        demo.Ids.ProductAId.Should().Be(
            DemoDataSeeder.ProductAId.Value);
        demo.DiscountCodes.Should().Contain("SAVE10");

        var scenario = await client.PostAsync(
            "/api/demo/scenarios/release-failure", null);
        (await scenario.Content
            .ReadFromJsonAsync<DemoScenarioResponse>())!
            .Scenario.Should().Be("release-failure");

        var recovery = await client.PostAsync(
            "/api/demo/reservation-recovery/run?maximumCount=10", null);
        recovery.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DemoEndpointsAreUnavailableInProduction()
    {
        using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();

        (await client.PostAsync("/api/demo/reset", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync(
            "/api/demo/scenarios/default", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void InterfaceAndConcreteAdaptersResolveToSameSingleton()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOrderRepository>()
            .Should().BeSameAs(scope.ServiceProvider
                .GetRequiredService<InMemoryOrderRepository>());
        scope.ServiceProvider
            .GetRequiredService<IInventoryReservationService>()
            .Should().BeSameAs(scope.ServiceProvider
                .GetRequiredService<
                    InMemoryInventoryReservationService>());
    }

    [Fact]
    public void ControllersAreSealedSenderBasedTransportAdapters()
    {
        Type[] controllers =
        [
            typeof(OrdersController),
            typeof(CheckoutController),
            typeof(PaymentsController),
            typeof(DemoController)
        ];

        controllers.Should().OnlyContain(type =>
            type.IsSealed && type.IsSubclassOf(typeof(ControllerBase)));
        var dependencies = controllers
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        controllers.Should().OnlyContain(type =>
            type.GetConstructors().Single().GetParameters()
                .Any(parameter =>
                    parameter.ParameterType == typeof(ISender)));
        dependencies.Should().NotContain(typeof(IOrderRepository));
        dependencies.Should().NotContain(type =>
            type.Name.EndsWith(
                "CommandHandler", StringComparison.Ordinal)
            || type.Name.EndsWith(
                "QueryHandler", StringComparison.Ordinal)
            || type.Name.EndsWith(
                "UseCase", StringComparison.Ordinal));
        typeof(CreateOrderRequest).GetProperties(
                BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.PropertyType)
            .Should().NotContain(type =>
                typeof(Order).IsAssignableFrom(type));
    }

    [Fact]
    public async Task UnsupportedScenarioUsesStableErrorEnvelope()
    {
        using var factory = new MarketplaceOrderingApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsync(
            "/api/demo/scenarios/unknown", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error =
            await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        error!.Code.Should().Be("demo.scenario_not_supported");
        error.Message.Should().NotBeNullOrWhiteSpace();
        error.Type.Should().Be("Validation");
        error.Metadata.Should().ContainKey("scenario");
    }

    [Fact]
    public async Task UnexpectedExceptionReturnsSafe500Envelope()
    {
        using var factory = new UnexpectedFailureApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(
            HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        var error =
            await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        error!.Code.Should().Be("api.unexpected_error");
        body.Should().NotContain("sensitive internal detail");
        body.Should().NotContain("InvalidOperationException");
    }
}

using System.Net;
using System.Reflection;
using FluentAssertions;
using MarketplaceOrdering.Api.Controllers;
using MarketplaceOrdering.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;

namespace MarketplaceOrdering.Api.Tests.Cancellation;

public sealed class ControllerCancellationTokenTests
{
    [Fact]
    public void EveryAsyncBusinessActionExposesRequiredCancellationToken()
    {
        var controllerTypes = new[]
        {
            typeof(OrdersController),
            typeof(CheckoutController),
            typeof(PaymentsController),
            typeof(DemoController)
        };

        var actions = controllerTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>()
                .Any())
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .ToArray();

        actions.Should().NotBeEmpty();
        var actionsWithOneRequiredToken = actions
            .Where(method =>
        {
            var parameters = method.GetParameters()
                .Where(parameter =>
                    parameter.ParameterType == typeof(CancellationToken))
                .ToArray();
            return parameters.Length == 1
                && !parameters[0].HasDefaultValue;
        })
            .ToArray();

        actionsWithOneRequiredToken.Should().HaveCount(actions.Length);
    }

    [Fact]
    public async Task RequestCancellationReachesApplicationPortAndPropagates()
    {
        await using var factory = new CancellationProbeApiFactory();
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();

        var responseTask = client.GetAsync(
            $"/api/orders/{Guid.NewGuid()}",
            cancellation.Token);
        await factory.Repository.Entered.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await FluentActions.Awaiting(() => responseTask)
            .Should().ThrowAsync<OperationCanceledException>();
        factory.Repository.CapturedCancellationToken.CanBeCanceled.Should()
            .BeTrue();
        factory.Repository.CapturedCancellationToken
            .IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationIsNotSerializedAsNormalApiErrorResponse()
    {
        await using var factory = new CancellationResponseApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be((HttpStatusCode)499);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PreCancelledRequestAbortedReachesApplicationCancellation()
    {
        await using var factory = new PreCancelledRequestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be((HttpStatusCode)499);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }
}

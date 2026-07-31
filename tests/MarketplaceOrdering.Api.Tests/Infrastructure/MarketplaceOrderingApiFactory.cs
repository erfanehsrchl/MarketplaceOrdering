using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MarketplaceOrdering.Api.Tests.Infrastructure;

internal sealed class MarketplaceOrderingApiFactory
    : WebApplicationFactory<Program>
{
    internal TestClock Clock { get; } = new(
        new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.RemoveAll<SystemClock>();
            services.AddSingleton(Clock);
            services.AddSingleton<IClock>(
                provider => provider.GetRequiredService<TestClock>());
        });
    }
}

internal sealed class ProductionApiFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
    }
}

internal sealed class UnexpectedFailureApiFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrderRepository>();
            services.AddSingleton<IOrderRepository, ThrowingOrderRepository>();
        });
    }

    private sealed class ThrowingOrderRepository : IOrderRepository
    {
        public Task<Result<Order>> LoadAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive internal detail");

        public Task<Result<long>> AddAsync(
            Order order,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive internal detail");

        public Task<Result<long>> SaveAsync(
            Order order,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive internal detail");

        public Task<Result<long>> SavePaymentAsync(
            Order order,
            TransactionId transactionId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive internal detail");
    }
}

internal sealed class CancellationProbeApiFactory
    : WebApplicationFactory<Program>
{
    internal CancellationProbeOrderRepository Repository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrderRepository>();
            services.AddSingleton<IOrderRepository>(Repository);
        });
    }
}

internal sealed class CancellationResponseApiFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrderRepository>();
            services.AddSingleton<IOrderRepository>(
                new ImmediateCancellationOrderRepository());
        });
    }
}

internal sealed class PreCancelledRequestApiFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, PreCancelledRequestFilter>());
    }

    private sealed class PreCancelledRequestFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next) =>
            application =>
            {
                application.Use(async (context, continuation) =>
                {
                    context.RequestAborted = new CancellationToken(
                        canceled: true);
                    await continuation();
                });
                next(application);
            };
    }
}

internal sealed class CancellationProbeOrderRepository : IOrderRepository
{
    private readonly TaskCompletionSource _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Entered => _entered.Task;
    internal CancellationToken CapturedCancellationToken { get; private set; }

    public async Task<Result<Order>> LoadAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        CapturedCancellationToken = cancellationToken;
        _entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }

    public Task<Result<long>> AddAsync(
        Order order,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Not used by this test.");

    public Task<Result<long>> SaveAsync(
        Order order,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Not used by this test.");

    public Task<Result<long>> SavePaymentAsync(
        Order order,
        TransactionId transactionId,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Not used by this test.");
}

internal sealed class ImmediateCancellationOrderRepository : IOrderRepository
{
    public Task<Result<Order>> LoadAsync(
        OrderId orderId,
        CancellationToken cancellationToken) =>
        throw new OperationCanceledException(cancellationToken);

    public Task<Result<long>> AddAsync(
        Order order,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Not used by this test.");

    public Task<Result<long>> SaveAsync(
        Order order,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Not used by this test.");

    public Task<Result<long>> SavePaymentAsync(
        Order order,
        TransactionId transactionId,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Not used by this test.");
}

internal sealed class TestClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}

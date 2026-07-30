using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Models;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Time;
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
        public Task<Result<VersionedOrder>> LoadAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive internal detail");

        public Task<Result<long>> AddAsync(
            Order order,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive internal detail");

        public Task<Result<long>> SaveAsync(
            Order order,
            long expectedVersion,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive internal detail");

        public Task<Result<long>> SavePaymentAsync(
            Order order,
            long expectedVersion,
            TransactionId transactionId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive internal detail");
    }
}

internal sealed class TestClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}

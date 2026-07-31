using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Application.Common.Abstractions.Events;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Offers;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Infrastructure.Discounts;
using MarketplaceOrdering.Infrastructure.Events;
using MarketplaceOrdering.Infrastructure.Idempotency;
using MarketplaceOrdering.Infrastructure.Inventory;
using MarketplaceOrdering.Infrastructure.Offers;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;
using MarketplaceOrdering.Infrastructure.Recovery;
using MarketplaceOrdering.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<SystemClock>();
        services.AddSingleton<IClock>(
            provider => provider.GetRequiredService<SystemClock>());
        services.AddSingleton<InMemoryDomainEventOutbox>();
        services.AddSingleton<IDomainEventOutbox>(
            provider => provider.GetRequiredService<
                InMemoryDomainEventOutbox>());
        services.AddSingleton<InMemoryOrderRepository>();
        services.AddSingleton<IOrderRepository>(
            provider => provider.GetRequiredService<InMemoryOrderRepository>());
        services.AddSingleton<InMemoryProductOfferProvider>();
        services.AddSingleton<IProductOfferProvider>(
            provider => provider.GetRequiredService<
                InMemoryProductOfferProvider>());
        services.AddSingleton<InMemoryDiscountPolicyProvider>();
        services.AddSingleton<IDiscountPolicyProvider>(
            provider => provider.GetRequiredService<
                InMemoryDiscountPolicyProvider>());
        services.AddSingleton<InMemoryInventoryReservationService>();
        services.AddSingleton<IInventoryReservationService>(
            provider => provider.GetRequiredService<
                InMemoryInventoryReservationService>());
        services.AddSingleton<InMemoryCheckoutIdempotencyStore>();
        services.AddSingleton<ICheckoutIdempotencyStore>(
            provider => provider.GetRequiredService<
                InMemoryCheckoutIdempotencyStore>());
        services.AddSingleton<InMemoryReservationRecoveryStore>();
        services.AddSingleton<IReservationRecoveryStore>(
            provider => provider.GetRequiredService<
                InMemoryReservationRecoveryStore>());
        return services;
    }
}

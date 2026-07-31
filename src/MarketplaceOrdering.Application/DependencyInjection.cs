using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(
                typeof(DependencyInjection).Assembly));
        services.AddSingleton<ProportionalDiscountAllocator>();
        services.AddSingleton<FulfillmentPlanner>();
        services.AddScoped<ReservationReleaseCoordinator>();
        return services;
    }
}

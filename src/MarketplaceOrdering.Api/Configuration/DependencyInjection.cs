using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using MarketplaceOrdering.Application.Checkout.RecoverOrphanReservations;
using MarketplaceOrdering.Application.Checkout.RetryPendingReservationReleases;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Offers;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Orders.AddOrderItem;
using MarketplaceOrdering.Application.Orders.ApplyDiscountCode;
using MarketplaceOrdering.Application.Orders.CancelOrder;
using MarketplaceOrdering.Application.Orders.ChangeOrderItemQuantity;
using MarketplaceOrdering.Application.Orders.CreateOrder;
using MarketplaceOrdering.Application.Orders.ExpireOrder;
using MarketplaceOrdering.Application.Orders.GetOrderDetails;
using MarketplaceOrdering.Application.Orders.RemoveDiscountCode;
using MarketplaceOrdering.Application.Orders.RemoveOrderItem;
using MarketplaceOrdering.Application.Payments.ConfirmPayment;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Infrastructure.Discounts;
using MarketplaceOrdering.Infrastructure.Idempotency;
using MarketplaceOrdering.Infrastructure.Inventory;
using MarketplaceOrdering.Infrastructure.Offers;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;
using MarketplaceOrdering.Infrastructure.Recovery;
using MarketplaceOrdering.Infrastructure.Time;

namespace MarketplaceOrdering.Api.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddSingleton<ProportionalDiscountAllocator>();
        services.AddSingleton<FulfillmentPlanner>();
        services.AddScoped<ReservationReleaseCoordinator>();
        services.AddScoped<CreateOrderUseCase>();
        services.AddScoped<AddOrderItemUseCase>();
        services.AddScoped<ChangeOrderItemQuantityUseCase>();
        services.AddScoped<RemoveOrderItemUseCase>();
        services.AddScoped<ApplyDiscountCodeUseCase>();
        services.AddScoped<RemoveDiscountCodeUseCase>();
        services.AddScoped<GetOrderDetailsUseCase>();
        services.AddScoped<CheckoutOrderUseCase>();
        services.AddScoped<ConfirmPaymentUseCase>();
        services.AddScoped<CancelOrderUseCase>();
        services.AddScoped<ExpireOrderUseCase>();
        services.AddScoped<RetryPendingReservationReleasesUseCase>();
        services.AddScoped<RecoverOrphanReservationsUseCase>();
        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<SystemClock>();
        services.AddSingleton<IClock>(
            provider => provider.GetRequiredService<SystemClock>());
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
        services.AddSingleton<DemoDataSeeder>();
        return services;
    }
}

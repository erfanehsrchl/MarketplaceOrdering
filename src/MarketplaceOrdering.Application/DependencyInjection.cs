using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using MarketplaceOrdering.Application.Checkout.RecoverOrphanReservations;
using MarketplaceOrdering.Application.Checkout.RetryPendingReservationReleases;
using MarketplaceOrdering.Application.Checkout.Services;
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
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Application;

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
}

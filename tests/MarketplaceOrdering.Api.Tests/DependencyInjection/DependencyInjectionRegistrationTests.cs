using FluentAssertions;
using MarketplaceOrdering.Application;
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
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Infrastructure;
using MarketplaceOrdering.Infrastructure.Discounts;
using MarketplaceOrdering.Infrastructure.Idempotency;
using MarketplaceOrdering.Infrastructure.Inventory;
using MarketplaceOrdering.Infrastructure.Offers;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;
using MarketplaceOrdering.Infrastructure.Recovery;
using MarketplaceOrdering.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceOrdering.Api.Tests.DependencyInjection;

public sealed class DependencyInjectionRegistrationTests
{
    private static readonly Type[] UseCaseTypes =
    [
        typeof(CreateOrderUseCase),
        typeof(AddOrderItemUseCase),
        typeof(ChangeOrderItemQuantityUseCase),
        typeof(RemoveOrderItemUseCase),
        typeof(ApplyDiscountCodeUseCase),
        typeof(RemoveDiscountCodeUseCase),
        typeof(GetOrderDetailsUseCase),
        typeof(CheckoutOrderUseCase),
        typeof(ConfirmPaymentUseCase),
        typeof(CancelOrderUseCase),
        typeof(ExpireOrderUseCase),
        typeof(RetryPendingReservationReleasesUseCase),
        typeof(RecoverOrphanReservationsUseCase)
    ];

    [Fact]
    public void CompleteModuleGraphValidatesAndResolvesRequiredServices()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        using var scope = provider.CreateScope();

        foreach (var useCaseType in UseCaseTypes)
            scope.ServiceProvider.GetRequiredService(useCaseType)
                .Should().NotBeNull();
        scope.ServiceProvider
            .GetRequiredService<ReservationReleaseCoordinator>()
            .Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<FulfillmentPlanner>()
            .Should().NotBeNull();
        scope.ServiceProvider
            .GetRequiredService<ProportionalDiscountAllocator>()
            .Should().NotBeNull();
    }

    [Fact]
    public void StatefulPortsAndConcreteAdaptersShareSingletonInstances()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        AssertShared<IOrderRepository, InMemoryOrderRepository>(provider);
        AssertShared<IProductOfferProvider,
            InMemoryProductOfferProvider>(provider);
        AssertShared<IDiscountPolicyProvider,
            InMemoryDiscountPolicyProvider>(provider);
        AssertShared<IInventoryReservationService,
            InMemoryInventoryReservationService>(provider);
        AssertShared<ICheckoutIdempotencyStore,
            InMemoryCheckoutIdempotencyStore>(provider);
        AssertShared<IReservationRecoveryStore,
            InMemoryReservationRecoveryStore>(provider);
        AssertShared<IClock, SystemClock>(provider);
    }

    [Fact]
    public void ModuleRegistrationsUseTheRequiredLifetimes()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();

        foreach (var useCaseType in UseCaseTypes)
            services.Single(descriptor =>
                    descriptor.ServiceType == useCaseType)
                .Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Single(descriptor => descriptor.ServiceType ==
                typeof(ReservationReleaseCoordinator))
            .Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Single(descriptor => descriptor.ServiceType ==
                typeof(FulfillmentPlanner))
            .Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Single(descriptor => descriptor.ServiceType ==
                typeof(ProportionalDiscountAllocator))
            .Lifetime.Should().Be(ServiceLifetime.Singleton);

        Type[] statefulAdapters =
        [
            typeof(InMemoryOrderRepository),
            typeof(InMemoryProductOfferProvider),
            typeof(InMemoryDiscountPolicyProvider),
            typeof(InMemoryInventoryReservationService),
            typeof(InMemoryCheckoutIdempotencyStore),
            typeof(InMemoryReservationRecoveryStore)
        ];
        foreach (var adapterType in statefulAdapters)
            services.Single(descriptor =>
                    descriptor.ServiceType == adapterType)
                .Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void RegistrationOwnershipAndAssemblyReferencesPointInward()
    {
        ((object)typeof(MarketplaceOrdering.Application.DependencyInjection)
                .Assembly)
            .Should().BeSameAs(typeof(CreateOrderUseCase).Assembly);
        ((object)typeof(MarketplaceOrdering.Infrastructure.DependencyInjection)
                .Assembly)
            .Should().BeSameAs(typeof(InMemoryOrderRepository).Assembly);

        ReferencedProjects(typeof(CreateOrderUseCase))
            .Should().NotContain([
                "MarketplaceOrdering.Infrastructure",
                "MarketplaceOrdering.Api"
            ]);
        ReferencedProjects(typeof(InMemoryOrderRepository))
            .Should().NotContain("MarketplaceOrdering.Api");
        var domainReferences = typeof(Order).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        domainReferences.Should().NotContain(reference =>
            reference != null
            && reference.StartsWith(
                "Microsoft.Extensions.DependencyInjection",
                StringComparison.Ordinal));
        domainReferences.Should().NotContain([
            "MarketplaceOrdering.Application",
            "MarketplaceOrdering.Infrastructure",
            "MarketplaceOrdering.Api"
        ]);
    }

    private static void AssertShared<TPort, TAdapter>(
        IServiceProvider provider)
        where TPort : notnull
        where TAdapter : class, TPort
    {
        var adapter = provider.GetRequiredService<TAdapter>();
        provider.GetRequiredService<TPort>().Should().BeSameAs(adapter);
        provider.GetRequiredService<TAdapter>().Should().BeSameAs(adapter);
    }

    private static string?[] ReferencedProjects(Type type) =>
        type.Assembly.GetReferencedAssemblies()
            .Where(reference => reference.Name?.StartsWith(
                "MarketplaceOrdering.",
                StringComparison.Ordinal) == true)
            .Select(reference => reference.Name)
            .ToArray();
}

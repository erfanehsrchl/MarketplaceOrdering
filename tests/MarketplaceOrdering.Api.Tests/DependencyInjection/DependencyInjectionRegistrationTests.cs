using MediatR;
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
    private static readonly Type[] HandlerTypes =
    [
        typeof(CreateOrderCommandHandler),
        typeof(AddOrderItemCommandHandler),
        typeof(ChangeOrderItemQuantityCommandHandler),
        typeof(RemoveOrderItemCommandHandler),
        typeof(ApplyDiscountCodeCommandHandler),
        typeof(RemoveDiscountCodeCommandHandler),
        typeof(GetOrderDetailsQueryHandler),
        typeof(CheckoutOrderCommandHandler),
        typeof(ConfirmPaymentCommandHandler),
        typeof(CancelOrderCommandHandler),
        typeof(ExpireOrderCommandHandler),
        typeof(RetryPendingReservationReleasesCommandHandler),
        typeof(RecoverOrphanReservationsCommandHandler)
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

        foreach (var handlerType in HandlerTypes)
            scope.ServiceProvider.GetRequiredService(
                    HandlerContract(handlerType))
                .Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ISender>()
            .Should().NotBeNull();
        scope.ServiceProvider
            .GetRequiredService<IReservationReleaseCoordinator>()
            .Should().BeOfType<ReservationReleaseCoordinator>();
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

        foreach (var handlerType in HandlerTypes)
            services.Single(descriptor =>
                    descriptor.ImplementationType == handlerType)
                .Lifetime.Should().Be(ServiceLifetime.Transient);
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType.Name.EndsWith(
                "UseCase", StringComparison.Ordinal)
            || (descriptor.ImplementationType != null
                && descriptor.ImplementationType.Name.EndsWith(
                    "UseCase", StringComparison.Ordinal)));
        var releaseCoordinator = services.Single(descriptor =>
            descriptor.ServiceType ==
                typeof(IReservationReleaseCoordinator));
        releaseCoordinator.ImplementationType.Should()
            .Be(typeof(ReservationReleaseCoordinator));
        releaseCoordinator.Lifetime.Should().Be(ServiceLifetime.Scoped);
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
            .Should().BeSameAs(typeof(CreateOrderCommandHandler).Assembly);
        ((object)typeof(MarketplaceOrdering.Infrastructure.DependencyInjection)
                .Assembly)
            .Should().BeSameAs(typeof(InMemoryOrderRepository).Assembly);

        ReferencedProjects(typeof(CreateOrderCommandHandler))
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
        domainReferences.Should().NotContain("MediatR");
        typeof(InMemoryOrderRepository).Assembly.GetTypes()
            .Should().NotContain(type => type.Name.EndsWith(
                "CommandHandler", StringComparison.Ordinal)
                || type.Name.EndsWith(
                    "QueryHandler", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingOutputPortRegistrationsAreDetectedDuringValidation()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        var build = () => services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        build.Should().Throw<AggregateException>();
    }

    [Fact]
    public void EveryCommandAndQueryHasExactlyOneResolvableHandler()
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

        var requests = typeof(CreateOrderCommandHandler).Assembly.GetTypes()
            .Where(type => !type.IsAbstract
                && type.GetInterfaces().Any(contract =>
                    contract.IsGenericType
                    && contract.GetGenericTypeDefinition()
                        == typeof(IRequest<>)))
            .ToArray();

        requests.Where(type => type.Name.EndsWith(
                "Command", StringComparison.Ordinal))
            .Should().HaveCount(13);
        requests.Where(type => type.Name.EndsWith(
                "Query", StringComparison.Ordinal))
            .Should().ContainSingle()
            .Which.Should().Be(typeof(GetOrderDetailsQuery));

        foreach (var request in requests)
        {
            var response = request.GetInterfaces().Single(contract =>
                    contract.IsGenericType
                    && contract.GetGenericTypeDefinition()
                        == typeof(IRequest<>))
                .GetGenericArguments()[0];
            var contract = typeof(IRequestHandler<,>)
                .MakeGenericType(request, response);
            services.Count(descriptor =>
                    descriptor.ServiceType == contract)
                .Should().Be(1);
            scope.ServiceProvider.GetRequiredService(contract)
                .Should().NotBeNull();
        }
    }

    [Fact]
    public async Task SenderPassesTheCallerCancellationTokenToTheHandler()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddTransient<
            IRequestHandler<CancellationProbeRequest, CancellationToken>,
            CancellationProbeHandler>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();

        var observed = await scope.ServiceProvider
            .GetRequiredService<ISender>()
            .Send(
                new CancellationProbeRequest(),
                cancellation.Token);

        observed.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task SenderDispatchesEveryApplicationCommandAndQuery()
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
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        object[] requests =
        [
            new CreateOrderCommand(Guid.Empty, string.Empty, null),
            new AddOrderItemCommand(
                Guid.Empty, Guid.Empty, string.Empty, 0),
            new ChangeOrderItemQuantityCommand(
                Guid.Empty, Guid.Empty, 0),
            new RemoveOrderItemCommand(Guid.Empty, Guid.Empty),
            new ApplyDiscountCodeCommand(Guid.Empty, string.Empty),
            new RemoveDiscountCodeCommand(Guid.Empty),
            new CheckoutOrderCommand(Guid.Empty, string.Empty),
            new ConfirmPaymentCommand(
                Guid.Empty, string.Empty, 0, default),
            new CancelOrderCommand(Guid.Empty, string.Empty),
            new ExpireOrderCommand(Guid.Empty),
            new RetryPendingReservationReleasesCommand(Guid.Empty),
            new RecoverOrphanReservationsCommand(0),
            new GetOrderDetailsQuery(Guid.Empty)
        ];

        foreach (var request in requests)
        {
            var result = await sender.Send(
                request, default);
            result.Should().NotBeNull();
        }
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

    private static Type HandlerContract(Type handlerType) =>
        handlerType.GetInterfaces().Single(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition()
                == typeof(IRequestHandler<,>));

    public sealed record CancellationProbeRequest
        : IRequest<CancellationToken>;

    public sealed class CancellationProbeHandler
        : IRequestHandler<CancellationProbeRequest, CancellationToken>
    {
        public Task<CancellationToken> Handle(
            CancellationProbeRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(cancellationToken);
    }
}

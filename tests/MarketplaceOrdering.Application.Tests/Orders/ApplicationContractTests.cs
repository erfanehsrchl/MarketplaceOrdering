using MediatR;
using System.Collections.ObjectModel;
using System.Reflection;
using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Offers;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Checkout.CheckoutOrder;
using MarketplaceOrdering.Application.Checkout.RetryPendingReservationReleases;
using MarketplaceOrdering.Application.Checkout.Services;
using MarketplaceOrdering.Application.Orders.AddOrderItem;
using MarketplaceOrdering.Application.Orders.ApplyDiscountCode;
using MarketplaceOrdering.Application.Orders.CancelOrder;
using MarketplaceOrdering.Application.Orders.ChangeOrderItemQuantity;
using MarketplaceOrdering.Application.Orders.CreateOrder;
using MarketplaceOrdering.Application.Orders.ExpireOrder;
using MarketplaceOrdering.Application.Orders.GetOrderDetails;
using MarketplaceOrdering.Application.Orders.Models;
using MarketplaceOrdering.Application.Orders.RemoveDiscountCode;
using MarketplaceOrdering.Application.Orders.RemoveOrderItem;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Domain.Discounts;
using MarketplaceOrdering.Domain.Fulfillment;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;

namespace MarketplaceOrdering.Application.Tests.Orders;

public sealed class ApplicationContractTests
{
    [Fact]
    public void Ports_ShouldBeInterfaces()
    {
        Type[] ports =
        [
            typeof(IClock),
            typeof(IOrderRepository),
            typeof(IProductOfferProvider),
            typeof(IDiscountPolicyProvider),
            typeof(IInventoryReservationService),
            typeof(ICheckoutIdempotencyStore),
            typeof(IReservationRecoveryStore)
        ];

        ports.Should().OnlyContain(type => type.IsInterface);
    }

    [Fact]
    public void Handlers_ShouldBeConcreteSealedClassesWithMediatRContracts()
    {
        Type[] handlers =
        [
            typeof(CreateOrderCommandHandler),
            typeof(AddOrderItemCommandHandler),
            typeof(ChangeOrderItemQuantityCommandHandler),
            typeof(RemoveOrderItemCommandHandler),
            typeof(ApplyDiscountCodeCommandHandler),
            typeof(RemoveDiscountCodeCommandHandler),
            typeof(GetOrderDetailsQueryHandler)
        ];

        handlers.Should().OnlyContain(type =>
            type.IsClass && type.IsSealed && !type.IsAbstract);
        handlers.Should().OnlyContain(type => type.GetInterfaces().Any(
            contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition()
                    == typeof(IRequestHandler<,>)));
    }

    [Fact]
    public void ReusableWorkflowService_ShouldExposeAnApplicationInterface()
    {
        typeof(IReservationReleaseCoordinator).IsInterface.Should().BeTrue();
        typeof(ReservationReleaseCoordinator).GetInterfaces()
            .Should().Contain(typeof(IReservationReleaseCoordinator));
        ((object)typeof(IReservationReleaseCoordinator).Assembly).Should()
            .BeSameAs(typeof(CreateOrderCommandHandler).Assembly);
        ((object)typeof(ReservationReleaseCoordinator).Assembly).Should()
            .BeSameAs(typeof(CreateOrderCommandHandler).Assembly);

        Type[] consumers =
        [
            typeof(CheckoutOrderCommandHandler),
            typeof(CancelOrderCommandHandler),
            typeof(ExpireOrderCommandHandler),
            typeof(RetryPendingReservationReleasesCommandHandler)
        ];
        consumers.SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Should().Contain(parameter =>
                parameter.ParameterType ==
                    typeof(IReservationReleaseCoordinator))
            .And.NotContain(parameter =>
                parameter.ParameterType ==
                    typeof(ReservationReleaseCoordinator));
    }

    [Fact]
    public void HandlersAndDeterministicAlgorithms_ShouldRemainConcreteOnly()
    {
        var applicationAssembly =
            typeof(CreateOrderCommandHandler).Assembly;
        applicationAssembly.GetTypes()
            .Where(type => type.Name.EndsWith(
                "CommandHandler", StringComparison.Ordinal)
                || type.Name.EndsWith(
                    "QueryHandler", StringComparison.Ordinal))
            .Should().NotContain(handler =>
                applicationAssembly.GetType(
                    $"{handler.Namespace}.I{handler.Name}") != null);
        typeof(FulfillmentPlanner).GetInterfaces().Should().BeEmpty();
        typeof(ProportionalDiscountAllocator).GetInterfaces()
            .Should().BeEmpty();
        ((object)typeof(IReservationReleaseCoordinator).Assembly).Should()
            .NotBeSameAs(typeof(InMemoryOrderRepository).Assembly);
        ((object)typeof(IReservationReleaseCoordinator).Assembly).Should()
            .NotBeSameAs(typeof(IDomainEvent).Assembly);
    }

    [Fact]
    public void InventoryAndIdempotencyOutcomes_ShouldBeStructurallySeparate()
    {
        typeof(InventoryReservationSucceeded).BaseType.Should()
            .Be(typeof(InventoryReservationOutcome));
        typeof(InventoryReservationRejected).BaseType.Should()
            .Be(typeof(InventoryReservationOutcome));
        typeof(InventoryReservationIndeterminate).BaseType.Should()
            .Be(typeof(InventoryReservationOutcome));
        typeof(CheckoutIdempotencyStarted).Should()
            .NotBe(typeof(CheckoutIdempotencyCompleted));
        typeof(CheckoutIdempotencyFailed).BaseType.Should()
            .Be(typeof(CheckoutIdempotencyClaim));
    }

    [Fact]
    public void OrderedApplicationCollections_ShouldUseReadOnlyListContracts()
    {
        IReadOnlyList<InventoryReservationItem> items =
        [
            new InventoryReservationItem(
                ProductId.Create(Guid.NewGuid()).Value,
                Quantity.Create(1).Value)
        ];
        var request = new InventoryReservationRequest(
            OrderId.New(),
            CheckoutAttemptId.New(),
            CheckoutTestVendor(),
            ReservationOperationKey.Create("operation").Value,
            items);

        typeof(CreateOrderCommand).GetProperty(
                nameof(CreateOrderCommand.Items))!.PropertyType
            .Should().Be(typeof(IReadOnlyList<CreateOrderItemInput>));
        typeof(InventoryReservationRequest).GetProperty(
                nameof(InventoryReservationRequest.Items))!.PropertyType
            .Should().Be(typeof(IReadOnlyList<InventoryReservationItem>));
        typeof(OrderDetails).GetProperty(
                nameof(OrderDetails.Items))!.PropertyType
            .Should().Be(typeof(IReadOnlyList<OrderItemDetails>));
        request.Items.Should().Equal(items);
    }

    [Fact]
    public void NewOrder_ShouldStartWithVersionZeroAndHideVersionSetter()
    {
        var order = ApplicationTestData.CreateOrder();

        order.Version.Should().Be(0);
        typeof(Order).GetProperty(nameof(Order.Version))!.SetMethod!
            .IsPublic.Should().BeFalse();
    }

    [Fact]
    public void PersistenceVersion_ShouldRejectNegativeValues()
    {
        var order = ApplicationTestData.CreateOrder();

        var update = () => order.UpdatePersistenceVersion(-1);

        update.Should().Throw<ArgumentOutOfRangeException>();
        order.Version.Should().Be(0);
    }

    [Fact]
    public void ApplicationAssembly_ShouldNotReferenceForbiddenLayers()
    {
        var references = typeof(CreateOrderCommandHandler).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        references.Any(name =>
            name != null && (
                name.Contains("AspNetCore", StringComparison.Ordinal)
                || name.Contains("Infrastructure", StringComparison.Ordinal)
                || name.Contains("EntityFrameworkCore", StringComparison.Ordinal)))
            .Should().BeFalse();
        references.Should().Contain("MediatR");
    }

    [Fact]
    public void RequestsAndHandlers_ShouldFollowCqrsAndLayerBoundaries()
    {
        var applicationAssembly =
            typeof(CreateOrderCommandHandler).Assembly;
        var requests = applicationAssembly.GetTypes()
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition()
                    == typeof(IRequest<>)))
            .ToArray();
        var handlers = applicationAssembly.GetTypes()
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition()
                    == typeof(IRequestHandler<,>)))
            .ToArray();

        requests.Should().HaveCount(13);
        requests.Should().OnlyContain(type =>
            type.Name.EndsWith("Command", StringComparison.Ordinal)
            || type.Name.EndsWith("Query", StringComparison.Ordinal));
        handlers.Should().HaveCount(13);
        handlers.Should().OnlyContain(type =>
            type.Name.EndsWith(
                "CommandHandler", StringComparison.Ordinal)
            || type.Name.EndsWith(
                "QueryHandler", StringComparison.Ordinal));

        var domainAssembly = typeof(IDomainEvent).Assembly;
        domainAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("MediatR");
        domainAssembly.GetTypes()
            .Where(typeof(IDomainEvent).IsAssignableFrom)
            .Should().NotContain(type =>
                typeof(INotification).IsAssignableFrom(type));
        typeof(InMemoryOrderRepository).Assembly.GetTypes()
            .Should().NotContain(type => type.Name.EndsWith(
                "Handler", StringComparison.Ordinal));
        typeof(InMemoryOrderRepository).Assembly.GetTypes()
            .Should().NotContain(type => type.Name == "StoredOrder");
        new[]
            {
                applicationAssembly,
                domainAssembly,
                typeof(InMemoryOrderRepository).Assembly
            }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name)
            .Should().NotContain(name => name != null
                && name.Contains(
                    "MassTransit", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestContracts_ShouldNotExposeMutableListsOrReadOnlyCollectionBackingFields()
    {
        var requestTypes = typeof(CreateOrderCommand).Assembly.GetTypes()
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition()
                    == typeof(IRequest<>)))
            .ToArray();

        requestTypes
            .SelectMany(type => type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            .Should().NotContain(property =>
                property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition()
                    == typeof(List<>));
        requestTypes
            .SelectMany(type => type.GetFields(
                BindingFlags.Instance
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly))
            .Should().NotContain(field =>
                field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition()
                    == typeof(ReadOnlyCollection<>));
    }

    private static VendorId CheckoutTestVendor() =>
        VendorId.Create(Guid.Parse(
            "60000000-0000-0000-0000-000000000000")).Value;
}

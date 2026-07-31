using MediatR;
using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Discounts;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Application.Common.Abstractions.Inventory;
using MarketplaceOrdering.Application.Common.Abstractions.Offers;
using MarketplaceOrdering.Application.Common.Abstractions.Persistence;
using MarketplaceOrdering.Application.Common.Abstractions.Recovery;
using MarketplaceOrdering.Application.Common.Abstractions.Time;
using MarketplaceOrdering.Application.Common.Models;
using MarketplaceOrdering.Application.Orders.AddOrderItem;
using MarketplaceOrdering.Application.Orders.ApplyDiscountCode;
using MarketplaceOrdering.Application.Orders.ChangeOrderItemQuantity;
using MarketplaceOrdering.Application.Orders.CreateOrder;
using MarketplaceOrdering.Application.Orders.GetOrderDetails;
using MarketplaceOrdering.Application.Orders.RemoveDiscountCode;
using MarketplaceOrdering.Application.Orders.RemoveOrderItem;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
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
    public void InventoryRequest_ShouldCopyItems()
    {
        var source = new[]
        {
            new InventoryReservationItem(
                ProductId.Create(Guid.NewGuid()).Value,
                Quantity.Create(1).Value)
        };
        var request = new InventoryReservationRequest(
            OrderId.New(),
            CheckoutAttemptId.New(),
            CheckoutTestVendor(),
            ReservationOperationKey.Create("operation").Value,
            source);
        source[0] = new InventoryReservationItem(
            ProductId.Create(Guid.NewGuid()).Value,
            Quantity.Create(2).Value);

        request.Items.Single().Quantity.Value.Should().Be(1);
        var action = () => ((ICollection<InventoryReservationItem>)
            request.Items).Clear();
        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void VersionedOrder_ShouldRequirePersistedVersion()
    {
        var action = () => new VersionedOrder(
            ApplicationTestData.CreateOrder(), 0);

        action.Should().Throw<ArgumentOutOfRangeException>();
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

    private static VendorId CheckoutTestVendor() =>
        VendorId.Create(Guid.Parse(
            "60000000-0000-0000-0000-000000000000")).Value;
}

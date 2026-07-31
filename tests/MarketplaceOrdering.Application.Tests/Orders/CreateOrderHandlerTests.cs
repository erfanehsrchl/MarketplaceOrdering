using FluentAssertions;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.CreateOrder;
using MarketplaceOrdering.Application.Tests.Fakes;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Application.Tests.Orders;

public sealed class CreateOrderCommandHandlerTests
{
    private static CreateOrderCommand ValidCommand() => new(
        Guid.Parse("20000000-0000-0000-0000-000000000000"),
        "20 Market Street",
        [
            new(Guid.Parse("30000000-0000-0000-0000-000000000000"),
                "First", 2),
            new(Guid.Parse("40000000-0000-0000-0000-000000000000"),
                "Second", 3)
        ]);

    [Fact]
    public void Command_ShouldPreserveScalarsAndOrderedApplicationInputs()
    {
        IReadOnlyList<CreateOrderItemInput> items =
        [
            new(Guid.NewGuid(), "First", 1),
            new(Guid.NewGuid(), "Second", 2)
        ];
        var customerId = Guid.NewGuid();

        var command = new CreateOrderCommand(
            customerId, "Address", items);

        command.CustomerId.Should().Be(customerId);
        command.DeliveryAddress.Should().Be("Address");
        command.Items.Should().BeSameAs(items);
        command.Items.Should().Equal(items);
    }

    [Fact]
    public async Task ValidCommand_ShouldCreatePersistAndMapOrder()
    {
        var repository = new FakeOrderRepository();
        var clock = new FakeClock();
        using var cancellation = new CancellationTokenSource();
        var useCase = new CreateOrderCommandHandler(repository, clock);

        var result = await useCase.Handle(
            ValidCommand(), cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().NotBeEmpty();
        result.Value.CreatedAt.Should().Be(clock.UtcNow);
        result.Value.Version.Should().Be(1);
        result.Value.Items.Select(item => item.ProductName)
            .Should().Equal("First", "Second");
        repository.AddCalls.Should().Be(1);
        repository.SaveCalls.Should().Be(0);
        repository.AddedOrder!.CreatedAt.Should().Be(result.Value.CreatedAt);
        repository.AddCancellationToken.Should().Be(cancellation.Token);

        repository.AddedOrder.ChangeItemQuantity(
            repository.AddedOrder.Items.First().ProductId,
            Quantity.Create(4).Value,
            clock.UtcNow.AddMinutes(1));
        result.Value.Items.First().Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Handler_ShouldNotMutateCommandItems()
    {
        var items = new List<CreateOrderItemInput>
        {
            new(Guid.NewGuid(), "First", 1),
            new(Guid.NewGuid(), "Second", 2)
        };
        var original = items.ToArray();
        var command = new CreateOrderCommand(
            Guid.NewGuid(), "Address", items);

        await new CreateOrderCommandHandler(
                new FakeOrderRepository(), new FakeClock())
            .Handle(command, CancellationToken.None);

        command.Items.Should().BeSameAs(items);
        command.Items.Should().Equal(original);
    }

    [Fact]
    public async Task DuplicateProducts_ShouldBeMergedByDomain()
    {
        var productId = Guid.NewGuid();
        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            "Address",
            [new(productId, "Original", 2), new(productId, "Ignored", 3)]);
        var repository = new FakeOrderRepository();

        var result = await new CreateOrderCommandHandler(repository, new FakeClock())
            .Handle(command, CancellationToken.None);

        result.Value.Items.Should().ContainSingle();
        result.Value.Items.Single().Quantity.Should().Be(5);
        result.Value.Items.Single().ProductName.Should().Be("Original");
    }

    public static TheoryData<CreateOrderCommand, string> InvalidCommands => new()
    {
        { new(Guid.Empty, "Address", [new(Guid.NewGuid(), "Product", 1)]), "customer_id.empty" },
        { new(Guid.NewGuid(), " ", [new(Guid.NewGuid(), "Product", 1)]), "delivery_address.empty" },
        { new(Guid.NewGuid(), "Address", [new(Guid.Empty, "Product", 1)]), "product_id.empty" },
        { new(Guid.NewGuid(), "Address", [new(Guid.NewGuid(), " ", 1)]), "product_name.empty" },
        { new(Guid.NewGuid(), "Address", [new(Guid.NewGuid(), "Product", 0)]), "quantity.not_positive" },
        { new(Guid.NewGuid(), "Address", []), "order.items_required" }
    };

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public async Task InvalidCommand_ShouldReturnFirstFailureWithoutPersistence(
        CreateOrderCommand command,
        string expectedCode)
    {
        var repository = new FakeOrderRepository();

        var result = await new CreateOrderCommandHandler(repository, new FakeClock())
            .Handle(command, CancellationToken.None);

        result.Error.Code.Should().Be(expectedCode);
        repository.AddCalls.Should().Be(0);
    }

    [Fact]
    public async Task NullItems_ShouldReturnApplicationInvalidRequest()
    {
        var repository = new FakeOrderRepository();
        var command = new CreateOrderCommand(Guid.NewGuid(), "Address", null);

        var result = await new CreateOrderCommandHandler(repository, new FakeClock())
            .Handle(command, CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.InvalidRequest);
        repository.AddCalls.Should().Be(0);
    }

    [Fact]
    public async Task AddFailure_ShouldBeReturned()
    {
        var repository = new FakeOrderRepository
        {
            AddFailure = ApplicationErrors.OrderAlreadyExists
        };

        var result = await new CreateOrderCommandHandler(repository, new FakeClock())
            .Handle(ValidCommand(), CancellationToken.None);

        result.Error.Should().Be(ApplicationErrors.OrderAlreadyExists);
        repository.AddCalls.Should().Be(1);
    }
}

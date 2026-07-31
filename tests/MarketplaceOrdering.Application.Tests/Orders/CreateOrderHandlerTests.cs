using FluentAssertions;
using MarketplaceOrdering.Application.Common.Errors;
using MarketplaceOrdering.Application.Orders.CreateOrder;
using MarketplaceOrdering.Application.Tests.Fakes;

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

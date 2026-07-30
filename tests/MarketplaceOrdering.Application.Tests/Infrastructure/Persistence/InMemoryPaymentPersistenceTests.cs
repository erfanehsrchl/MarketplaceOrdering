using FluentAssertions;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Persistence.InMemory;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Persistence;

public sealed class InMemoryPaymentPersistenceTests
{
    [Fact]
    public async Task TransactionClaimAndPaymentSaveAreAtomicAndGlobal()
    {
        var repository = new InMemoryOrderRepository();
        var transactionId = TransactionId.Create("tx-one").Value;
        var first = InfrastructureTestData.Order();
        var second = InfrastructureTestData.Order();
        await repository.AddAsync(first, default);
        await repository.AddAsync(second, default);
        InfrastructureTestData.MakePaid(first, transactionId);
        InfrastructureTestData.MakePaid(second, transactionId);

        var winner = await repository.SavePaymentAsync(
            first, 1, transactionId, default);
        var loser = await repository.SavePaymentAsync(
            second, 1, transactionId, default);

        winner.Value.Should().Be(2);
        loser.Error.Code.Should().Be(
            "payment.transaction_id_already_used");
        second.DomainEvents.Should().NotBeEmpty();
        (await repository.LoadAsync(second.Id, default)).Value.Order.Status
            .Should().Be(Domain.Orders.OrderStatus.Draft);
    }

    [Fact]
    public async Task VersionConflictDoesNotClaimTransaction()
    {
        var repository = new InMemoryOrderRepository();
        var transactionId = TransactionId.Create("tx-stale").Value;
        var stale = InfrastructureTestData.Order();
        var valid = InfrastructureTestData.Order();
        await repository.AddAsync(stale, default);
        await repository.AddAsync(valid, default);
        InfrastructureTestData.MakePaid(stale, transactionId);
        InfrastructureTestData.MakePaid(valid, transactionId);

        (await repository.SavePaymentAsync(stale, 0, transactionId, default))
            .Error.Code.Should().Be("order.version_conflict");
        var validSave = await repository.SavePaymentAsync(
            valid, 1, transactionId, default);
        validSave.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SamePersistedPaymentReplayIsANoOp()
    {
        var repository = new InMemoryOrderRepository();
        var transactionId = TransactionId.Create("tx-replay").Value;
        var order = InfrastructureTestData.Order();
        await repository.AddAsync(order, default);
        InfrastructureTestData.MakePaid(order, transactionId);
        await repository.SavePaymentAsync(order, 1, transactionId, default);
        var loaded = await repository.LoadAsync(order.Id, default);

        var replay = await repository.SavePaymentAsync(
            loaded.Value.Order, 2, transactionId, default);

        replay.Value.Should().Be(2);
        (await repository.LoadAsync(order.Id, default)).Value.Version
            .Should().Be(2);
    }
}

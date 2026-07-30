using FluentAssertions;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Recovery;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Recovery;

public sealed class InMemoryReservationRecoveryStoreTests
{
    [Fact]
    public async Task UpsertUpdatesProgressAndReturnsDeterministicCopies()
    {
        var store = new InMemoryReservationRecoveryStore();
        var laterKey = ReservationOperationKey.Create("b").Value;
        var earlierKey = ReservationOperationKey.Create("a").Value;
        var later = InfrastructureTestData.RecoveryRecord(
            laterKey, createdAt: InfrastructureTestData.Now.AddMinutes(1));
        var earlier = InfrastructureTestData.RecoveryRecord(
            earlierKey, createdAt: InfrastructureTestData.Now);
        await store.UpsertAsync(later, default);
        await store.UpsertAsync(earlier, default);
        await store.UpsertAsync(earlier with
        {
            LastErrorCode = "release.retry",
            AttemptCount = 2
        }, default);

        var result = await store.GetPendingAsync(10, default);

        result.Value.Select(record => record.OperationKey)
            .Should().ContainInOrder(earlierKey, laterKey);
        result.Value.First().AttemptCount.Should().Be(2);
        result.Value.Should().NotBeSameAs(
            (await store.GetPendingAsync(10, default)).Value);
    }

    [Fact]
    public async Task IdentityConflictDoesNotReplaceRecord()
    {
        var store = new InMemoryReservationRecoveryStore();
        var key = ReservationOperationKey.Create("key").Value;
        var record = InfrastructureTestData.RecoveryRecord(key);
        await store.UpsertAsync(record, default);

        var conflict = await store.UpsertAsync(
            record with { OrderId = OrderId.New() }, default);

        conflict.Error.Code.Should().Be("recovery.record_conflict");
        (await store.GetPendingAsync(10, default)).Value.Single().OrderId
            .Should().Be(record.OrderId);
    }

    [Fact]
    public async Task MaximumCountAndResolutionSemanticsAreEnforced()
    {
        var store = new InMemoryReservationRecoveryStore();
        var key = ReservationOperationKey.Create("key").Value;
        await store.UpsertAsync(
            InfrastructureTestData.RecoveryRecord(key), default);

        (await store.GetPendingAsync(0, default)).Error.Code
            .Should().Be("recovery.maximum_count_invalid");
        (await store.GetPendingAsync(-1, default)).Error.Code
            .Should().Be("recovery.maximum_count_invalid");
        (await store.GetPendingAsync(1, default)).Value
            .Should().ContainSingle();
        (await store.MarkResolvedAsync(key, default))
            .IsSuccess.Should().BeTrue();
        (await store.MarkResolvedAsync(key, default))
            .IsSuccess.Should().BeTrue();
        (await store.GetPendingAsync(10, default)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var store = new InMemoryReservationRecoveryStore();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.GetPendingAsync(1, new CancellationToken(true)));
    }
}

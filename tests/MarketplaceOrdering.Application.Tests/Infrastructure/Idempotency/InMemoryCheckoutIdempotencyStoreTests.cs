using FluentAssertions;
using MarketplaceOrdering.Application.Common.Abstractions.Idempotency;
using MarketplaceOrdering.Domain.Shared;
using MarketplaceOrdering.Domain.ValueObjects;
using MarketplaceOrdering.Infrastructure.Idempotency;

namespace MarketplaceOrdering.Application.Tests.Infrastructure.Idempotency;

public sealed class InMemoryCheckoutIdempotencyStoreTests
{
    [Fact]
    public async Task ClaimTransitionsAndExactReplaysAreStable()
    {
        var store = new InMemoryCheckoutIdempotencyStore();
        var key = IdempotencyKey.Create("checkout").Value;
        var orderId = OrderId.New();
        var attemptId = CheckoutAttemptId.New();
        var started = await store.TryBeginAsync(
            key, orderId, attemptId, InfrastructureTestData.Now, default);
        started.Value.Should().BeOfType<CheckoutIdempotencyStarted>();
        (await store.TryBeginAsync(
            key, orderId, CheckoutAttemptId.New(),
            InfrastructureTestData.Now, default)).Value
            .Should().BeOfType<CheckoutIdempotencyInProgress>()
            .Which.CheckoutAttemptId.Should().Be(attemptId);

        var result = InfrastructureTestData.CheckoutResult(
            orderId, attemptId);
        (await store.CompleteAsync(
            key, result, InfrastructureTestData.Now, default))
            .IsSuccess.Should().BeTrue();
        (await store.CompleteAsync(
            key, result, InfrastructureTestData.Now.AddHours(1), default))
            .IsSuccess.Should().BeTrue();
        (await store.TryBeginAsync(
            key, orderId, CheckoutAttemptId.New(),
            InfrastructureTestData.Now, default)).Value
            .Should().Be(new CheckoutIdempotencyCompleted(result));
    }

    [Fact]
    public async Task DifferentOrderConflictsAndTerminalTransitionsAreRejected()
    {
        var store = new InMemoryCheckoutIdempotencyStore();
        var key = IdempotencyKey.Create("checkout").Value;
        var orderId = OrderId.New();
        var attemptId = CheckoutAttemptId.New();
        await store.TryBeginAsync(
            key, orderId, attemptId, InfrastructureTestData.Now, default);

        (await store.TryBeginAsync(
            key, OrderId.New(), CheckoutAttemptId.New(),
            InfrastructureTestData.Now, default)).Value
            .Should().BeOfType<CheckoutIdempotencyConflict>();
        await store.FailAsync(key,
            Error.DependencyFailure("checkout.failed", "Failed."),
            InfrastructureTestData.Now, default);
        (await store.CompleteAsync(
            key, InfrastructureTestData.CheckoutResult(orderId, attemptId),
            InfrastructureTestData.Now, default)).Error.Code
            .Should().Be("idempotency.invalid_transition");
    }

    [Fact]
    public async Task FailureReplayUsesSemanticErrorEquality()
    {
        var store = new InMemoryCheckoutIdempotencyStore();
        var key = IdempotencyKey.Create("failure").Value;
        await store.TryBeginAsync(
            key, OrderId.New(), CheckoutAttemptId.New(),
            InfrastructureTestData.Now, default);
        var first = Error.DependencyFailure(
            "checkout.failed", "Failed.",
            new Dictionary<string, string> { ["vendor"] = "one" });
        var equivalent = Error.DependencyFailure(
            "checkout.failed", "Failed.",
            new Dictionary<string, string> { ["vendor"] = "one" });

        (await store.FailAsync(
            key, first, InfrastructureTestData.Now, default))
            .IsSuccess.Should().BeTrue();
        (await store.FailAsync(
            key, equivalent, InfrastructureTestData.Now.AddHours(1), default))
            .IsSuccess.Should().BeTrue();
        (await store.FailAsync(
            key, Error.DependencyFailure("other", "Other."),
            InfrastructureTestData.Now, default)).Error.Code
            .Should().Be("idempotency.entry_conflict");
    }

    [Fact]
    public async Task ConcurrentClaimHasExactlyOneStarted()
    {
        var store = new InMemoryCheckoutIdempotencyStore();
        var key = IdempotencyKey.Create("parallel").Value;
        var orderId = OrderId.New();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            store.TryBeginAsync(
                key, orderId, CheckoutAttemptId.New(),
                InfrastructureTestData.Now, default)));

        var claims = (await Task.WhenAll(tasks))
            .Select(result => result.Value).ToArray();
        claims.OfType<CheckoutIdempotencyStarted>()
            .Should().ContainSingle();
        claims.OfType<CheckoutIdempotencyInProgress>()
            .Should().HaveCount(7);
        claims.Select(claim => claim switch
            {
                CheckoutIdempotencyStarted started =>
                    started.CheckoutAttemptId,
                CheckoutIdempotencyInProgress active =>
                    active.CheckoutAttemptId,
                _ => default
            }).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task MissingTargetsAndCancellationFail()
    {
        var store = new InMemoryCheckoutIdempotencyStore();
        var key = IdempotencyKey.Create("missing").Value;
        var order = OrderId.New();
        var attempt = CheckoutAttemptId.New();
        (await store.CompleteAsync(
            key, InfrastructureTestData.CheckoutResult(order, attempt),
            InfrastructureTestData.Now, default)).Error.Code
            .Should().Be("idempotency.entry_not_found");
        (await store.FailAsync(
            key, Error.DependencyFailure("x", "X."),
            InfrastructureTestData.Now, default)).Error.Code
            .Should().Be("idempotency.entry_not_found");
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.TryBeginAsync(
                key, order, attempt, InfrastructureTestData.Now,
                new CancellationToken(true)));
    }
}

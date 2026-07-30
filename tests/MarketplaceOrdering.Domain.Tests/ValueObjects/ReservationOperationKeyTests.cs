using FluentAssertions;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.ValueObjects;

public sealed class ReservationOperationKeyTests
{
    private static readonly OrderId Order =
        OrderId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111")).Value;
    private static readonly CheckoutAttemptId Attempt =
        CheckoutAttemptId.Create(Guid.Parse("22222222-2222-2222-2222-222222222222")).Value;
    private static readonly VendorId Vendor =
        VendorId.Create(Guid.Parse("33333333-3333-3333-3333-333333333333")).Value;

    [Fact]
    public void Create_ShouldValidateAndTrim()
    {
        ReservationOperationKey.Create(null).IsFailure.Should().BeTrue();
        ReservationOperationKey.Create("").IsFailure.Should().BeTrue();
        ReservationOperationKey.Create("  ").IsFailure.Should().BeTrue();

        var key = ReservationOperationKey.Create("  reservation:key  ").Value;
        key.Value.Should().Be("reservation:key");
        key.Should().Be(ReservationOperationKey.Create("reservation:key").Value);
    }

    [Fact]
    public void For_ShouldUseDocumentedDeterministicFormat()
    {
        var first = ReservationOperationKey.For(Order, Attempt, Vendor);
        var second = ReservationOperationKey.For(Order, Attempt, Vendor);

        first.Should().Be(second);
        first.Value.Should().Be(
            "reservation:11111111111111111111111111111111:" +
            "22222222222222222222222222222222:" +
            "33333333333333333333333333333333");
        first.ToString().Should().Be(first.Value);
    }

    [Fact]
    public void For_ShouldChangeWhenAnyIdentifierChanges()
    {
        var baseline = ReservationOperationKey.For(Order, Attempt, Vendor);

        ReservationOperationKey.For(OrderId.New(), Attempt, Vendor).Should().NotBe(baseline);
        ReservationOperationKey.For(Order, CheckoutAttemptId.New(), Vendor).Should().NotBe(baseline);
        ReservationOperationKey.For(Order, Attempt, VendorId.Create(Guid.NewGuid()).Value)
            .Should().NotBe(baseline);
    }

    [Fact]
    public void For_ShouldContainNoTimeOrRandomComponent()
    {
        var values = Enumerable.Range(0, 10)
            .Select(_ => ReservationOperationKey.For(Order, Attempt, Vendor).Value)
            .ToArray();

        values.Should().OnlyContain(value => value == values[0]);
        values[0].Should().MatchRegex(
            "^reservation:[0-9a-f]{32}:[0-9a-f]{32}:[0-9a-f]{32}$");
    }
}

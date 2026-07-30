using FluentAssertions;
using MarketplaceOrdering.Domain.Checkout;
using MarketplaceOrdering.Domain.Orders;
using MarketplaceOrdering.Domain.Orders.Events;
using MarketplaceOrdering.Domain.Tests.TestFixtures;
using MarketplaceOrdering.Domain.ValueObjects;

namespace MarketplaceOrdering.Domain.Tests.Checkout;

public sealed class OrderCheckoutTests
{
    [Fact]
    public void StartCheckout_ShouldEnterProcessingAndRaiseEvent()
    {
        var order = OrderTestData.CreateOrder();
        var attemptId = CheckoutAttemptId.New();
        order.ClearCommittedDomainEvents();

        var result = order.StartCheckout(attemptId, CheckoutTestData.StartedAt);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Processing);
        order.CheckoutAttempt.Should().NotBeNull();
        order.CheckoutAttempt!.Id.Should().Be(attemptId);
        order.CheckoutAttempt.Status.Should().Be(CheckoutAttemptStatus.Planning);
        order.CheckoutAttempt.StartedAt.Should().Be(CheckoutTestData.StartedAt);
        var raised = order.DomainEvents.Should()
            .ContainSingle().Which.Should()
            .BeOfType<OrderSubmittedForProcessingDomainEvent>().Which;
        raised.OccurredAt.Should().Be(CheckoutTestData.StartedAt);
        raised.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public void StartCheckoutWhileProcessing_ShouldNotReplaceAttemptOrRaiseEvent()
    {
        var order = OrderTestData.CreateOrder();
        var original = CheckoutAttemptId.New();
        order.StartCheckout(original, CheckoutTestData.StartedAt);
        order.ClearCommittedDomainEvents();

        var result = order.StartCheckout(
            CheckoutAttemptId.New(), CheckoutTestData.StartedAt.AddMinutes(1));

        result.Error.Should().Be(CheckoutErrors.AlreadyInProgress);
        order.CheckoutAttempt!.Id.Should().Be(original);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void DemandSnapshot_ShouldBeCopiedAndPreserveProductSnapshots()
    {
        var order = OrderTestData.CreateOrder(
            OrderTestData.Initial(1, 2, "Original name"));

        var first = order.GetDemandSnapshot();
        var second = order.GetDemandSnapshot();

        first.Should().NotBeSameAs(second);
        first.Single().Product.ProductName.Value.Should().Be("Original name");
        first.Single().Quantity.Value.Should().Be(2);
    }

    [Fact]
    public void ValidPlan_ShouldAttachOnceAndRaiseSummaryEvent()
    {
        var data = CheckoutTestData.StartedWithPlan();

        data.Order.CheckoutAttempt!.Status.Should()
            .Be(CheckoutAttemptStatus.Reserving);
        data.Order.CheckoutAttempt.FulfillmentPlan.Should().BeSameAs(data.Plan);
        data.Order.DomainEvents.OfType<FulfillmentPlanCreatedDomainEvent>()
            .Should().ContainSingle()
            .Which.TotalPayable.Should().Be(data.Plan.TotalPayable);

        var second = data.Order.AttachFulfillmentPlan(
            data.AttemptId, data.Plan, CheckoutTestData.StartedAt.AddMinutes(3));
        second.Error.Should().Be(CheckoutErrors.PlanAlreadyAttached);
    }

    [Fact]
    public void PlanForDifferentDemand_ShouldFailWithoutMutationOrEvent()
    {
        var target = OrderTestData.CreateOrder(OrderTestData.Initial(2, 1));
        var other = CheckoutTestData.StartedWithPlan();
        var attemptId = CheckoutAttemptId.New();
        target.StartCheckout(attemptId, CheckoutTestData.StartedAt);
        target.ClearCommittedDomainEvents();

        var result = target.AttachFulfillmentPlan(
            attemptId, other.Plan, CheckoutTestData.StartedAt);

        result.Error.Should().Be(CheckoutErrors.PlanDoesNotMatchOrder);
        target.CheckoutAttempt!.Status.Should().Be(CheckoutAttemptStatus.Planning);
        target.CheckoutAttempt.FulfillmentPlan.Should().BeNull();
        target.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void NullPlanAndWrongAttempt_ShouldReturnStableFailures()
    {
        var order = OrderTestData.CreateOrder();
        var attemptId = CheckoutAttemptId.New();
        order.StartCheckout(attemptId, CheckoutTestData.StartedAt);

        order.AttachFulfillmentPlan(
            attemptId, null, CheckoutTestData.StartedAt)
            .Error.Should().Be(CheckoutErrors.PlanRequired);
        order.AttachFulfillmentPlan(
            CheckoutAttemptId.New(), null, CheckoutTestData.StartedAt)
            .Error.Should().Be(CheckoutErrors.AttemptMismatch);
    }

    [Fact]
    public void CheckoutProcessing_ShouldKeepDraftEditingLocked()
    {
        var data = CheckoutTestData.StartedWithPlan();

        data.Order.AddItem(
            OrderTestData.Product(2),
            OrderTestData.Quantity(1),
            CheckoutTestData.StartedAt).Error.Code.Should().Be("order.not_editable");
        data.Order.RemoveDiscountCode(
            CheckoutTestData.StartedAt).Error.Code.Should().Be("order.not_editable");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("quantity")]
    [InlineData("name")]
    public void PlanDemandMismatch_ShouldBeRejected(string mismatch)
    {
        var target = mismatch switch
        {
            "missing" => OrderTestData.CreateOrder(
                OrderTestData.Initial(1), OrderTestData.Initial(2)),
            "quantity" => OrderTestData.CreateOrder(OrderTestData.Initial(1, 2)),
            _ => OrderTestData.CreateOrder(OrderTestData.Initial(1, 1, "Target"))
        };
        var source = mismatch switch
        {
            "extra" => OrderTestData.CreateOrder(
                OrderTestData.Initial(1, 1, "Target"),
                OrderTestData.Initial(2)),
            "name" => OrderTestData.CreateOrder(
                OrderTestData.Initial(1, 1, "Different")),
            _ => OrderTestData.CreateOrder(OrderTestData.Initial(1))
        };
        var plan = CheckoutTestData.PlanFor(source);
        var attemptId = CheckoutAttemptId.New();
        target.StartCheckout(attemptId, CheckoutTestData.StartedAt);
        target.ClearCommittedDomainEvents();

        var result = target.AttachFulfillmentPlan(
            attemptId, plan, CheckoutTestData.StartedAt);

        result.Error.Should().Be(CheckoutErrors.PlanDoesNotMatchOrder);
        target.CheckoutAttempt!.FulfillmentPlan.Should().BeNull();
        target.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void FailedAttempt_ShouldBeReplaceable()
    {
        var order = OrderTestData.CreateOrder();
        var firstId = CheckoutAttemptId.New();
        order.StartCheckout(firstId, CheckoutTestData.StartedAt);
        var failure = CheckoutFailure.Create(
            "fulfillment.no_valid_plan", CheckoutTestData.StartedAt).Value;
        order.FailCheckoutBeforeReservations(
            firstId, failure, CheckoutTestData.StartedAt);
        var secondId = CheckoutAttemptId.New();

        order.StartCheckout(
            secondId,
            CheckoutTestData.StartedAt.AddMinutes(1)).IsSuccess.Should().BeTrue();

        order.CheckoutAttempt!.Id.Should().Be(secondId);
        order.CheckoutAttempt.Status.Should().Be(CheckoutAttemptStatus.Planning);
    }
}

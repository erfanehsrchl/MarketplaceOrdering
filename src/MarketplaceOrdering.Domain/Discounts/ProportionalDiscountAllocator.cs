using System.Numerics;
using MarketplaceOrdering.Domain.Shared;
using MoneyValue = MarketplaceOrdering.Domain.Money.Money;

namespace MarketplaceOrdering.Domain.Discounts;

public sealed class ProportionalDiscountAllocator
{
    public Result<IReadOnlyCollection<VendorDiscountAllocation>> Allocate(
        MoneyValue totalDiscount,
        IReadOnlyCollection<VendorProductAmount>? eligibleVendorAmounts)
    {
        if (totalDiscount.Amount == 0)
        {
            return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Success(
                Array.Empty<VendorDiscountAllocation>());
        }

        if (eligibleVendorAmounts is null || eligibleVendorAmounts.Count == 0)
        {
            return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Failure(
                DiscountErrors.AllocationFailed);
        }

        var orderedAmounts = eligibleVendorAmounts
            .OrderBy(amount => amount.VendorId.Value)
            .ToArray();

        if (orderedAmounts.Select(amount => amount.VendorId).Distinct().Count()
            != orderedAmounts.Length)
        {
            return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Failure(
                DiscountErrors.AllocationFailed);
        }

        var totalEligibleAmount = orderedAmounts.Aggregate(
            BigInteger.Zero,
            (sum, amount) => sum + amount.ProductsAmount.Amount);

        if (totalEligibleAmount <= BigInteger.Zero)
        {
            return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Failure(
                DiscountErrors.AllocationFailed);
        }

        var states = new List<AllocationState>(orderedAmounts.Length);
        var allocatedTotal = BigInteger.Zero;
        foreach (var vendorAmount in orderedAmounts)
        {
            var numerator =
                (BigInteger)totalDiscount.Amount
                * vendorAmount.ProductsAmount.Amount;
            var quotient = BigInteger.DivRem(
                numerator,
                totalEligibleAmount,
                out var remainder);

            if (quotient < BigInteger.Zero || quotient > long.MaxValue)
            {
                return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Failure(
                    DiscountErrors.AllocationFailed);
            }

            var allocatedAmount = (long)quotient;
            states.Add(new AllocationState(
                vendorAmount,
                remainder,
                allocatedAmount));
            allocatedTotal += quotient;
        }

        var remaining = (BigInteger)totalDiscount.Amount - allocatedTotal;
        if (remaining < BigInteger.Zero || remaining > states.Count)
        {
            return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Failure(
                DiscountErrors.AllocationFailed);
        }

        var remainderOrder = states
            .OrderByDescending(state => state.Remainder)
            .ThenBy(state => state.VendorAmount.VendorId.Value)
            .ToArray();

        for (var index = 0; index < (int)remaining; index++)
        {
            try
            {
                remainderOrder[index].AllocatedAmount =
                    checked(remainderOrder[index].AllocatedAmount + 1);
            }
            catch (OverflowException)
            {
                return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Failure(
                    DiscountErrors.AllocationFailed);
            }
        }

        var enforceVendorCaps = totalDiscount.Amount <= totalEligibleAmount;
        if (!InvariantsHold(states, totalDiscount.Amount, enforceVendorCaps))
        {
            return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Failure(
                DiscountErrors.AllocationFailed);
        }

        var allocations = states
            .Where(state => state.AllocatedAmount > 0)
            .OrderBy(state => state.VendorAmount.VendorId.Value)
            .Select(state => new VendorDiscountAllocation(
                state.VendorAmount.VendorId,
                MoneyValue.Create(state.AllocatedAmount).Value))
            .ToArray();

        return Result<IReadOnlyCollection<VendorDiscountAllocation>>.Success(
            Array.AsReadOnly(allocations));
    }

    private static bool InvariantsHold(
        IEnumerable<AllocationState> states,
        long expectedTotal,
        bool enforceVendorCaps)
    {
        var sum = BigInteger.Zero;
        foreach (var state in states)
        {
            if (state.AllocatedAmount < 0
                || (enforceVendorCaps
                    && state.AllocatedAmount
                        > state.VendorAmount.ProductsAmount.Amount))
            {
                return false;
            }

            sum += state.AllocatedAmount;
        }

        return sum == expectedTotal;
    }

    private sealed class AllocationState(
        VendorProductAmount vendorAmount,
        BigInteger remainder,
        long allocatedAmount)
    {
        internal VendorProductAmount VendorAmount { get; } = vendorAmount;

        internal BigInteger Remainder { get; } = remainder;

        internal long AllocatedAmount { get; set; } = allocatedAmount;
    }
}

namespace Payments.Domain.Tests;

public class PaymentTransitionsTests
{
    private const long SameAmountRefunded = 1_000;

    public static readonly TheoryData<PaymentStatus, PaymentStatus, TransitionOutcome> ExpectedOutcomes = new()
    {
        { PaymentStatus.Created, PaymentStatus.Created, TransitionOutcome.Redundant },
        { PaymentStatus.Created, PaymentStatus.RequiresAction, TransitionOutcome.Applied },
        { PaymentStatus.Created, PaymentStatus.Processing, TransitionOutcome.Applied },
        { PaymentStatus.Created, PaymentStatus.Failed, TransitionOutcome.Applied },
        { PaymentStatus.Created, PaymentStatus.Paid, TransitionOutcome.Applied },
        { PaymentStatus.Created, PaymentStatus.Canceled, TransitionOutcome.Applied },
        { PaymentStatus.Created, PaymentStatus.PartiallyRefunded, TransitionOutcome.Applied },
        { PaymentStatus.Created, PaymentStatus.Refunded, TransitionOutcome.Applied },

        { PaymentStatus.RequiresAction, PaymentStatus.Created, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.RequiresAction, PaymentStatus.RequiresAction, TransitionOutcome.Redundant },
        { PaymentStatus.RequiresAction, PaymentStatus.Processing, TransitionOutcome.Applied },
        { PaymentStatus.RequiresAction, PaymentStatus.Failed, TransitionOutcome.Applied },
        { PaymentStatus.RequiresAction, PaymentStatus.Paid, TransitionOutcome.Applied },
        { PaymentStatus.RequiresAction, PaymentStatus.Canceled, TransitionOutcome.Applied },
        { PaymentStatus.RequiresAction, PaymentStatus.PartiallyRefunded, TransitionOutcome.Applied },
        { PaymentStatus.RequiresAction, PaymentStatus.Refunded, TransitionOutcome.Applied },

        { PaymentStatus.Processing, PaymentStatus.Created, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Processing, PaymentStatus.RequiresAction, TransitionOutcome.Applied },
        { PaymentStatus.Processing, PaymentStatus.Processing, TransitionOutcome.Redundant },
        { PaymentStatus.Processing, PaymentStatus.Failed, TransitionOutcome.Applied },
        { PaymentStatus.Processing, PaymentStatus.Paid, TransitionOutcome.Applied },
        { PaymentStatus.Processing, PaymentStatus.Canceled, TransitionOutcome.Applied },
        { PaymentStatus.Processing, PaymentStatus.PartiallyRefunded, TransitionOutcome.Applied },
        { PaymentStatus.Processing, PaymentStatus.Refunded, TransitionOutcome.Applied },

        { PaymentStatus.Failed, PaymentStatus.Created, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Failed, PaymentStatus.RequiresAction, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Failed, PaymentStatus.Processing, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Failed, PaymentStatus.Failed, TransitionOutcome.Redundant },
        { PaymentStatus.Failed, PaymentStatus.Paid, TransitionOutcome.Applied },
        { PaymentStatus.Failed, PaymentStatus.Canceled, TransitionOutcome.Applied },
        { PaymentStatus.Failed, PaymentStatus.PartiallyRefunded, TransitionOutcome.Applied },
        { PaymentStatus.Failed, PaymentStatus.Refunded, TransitionOutcome.Applied },

        { PaymentStatus.Paid, PaymentStatus.Created, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Paid, PaymentStatus.RequiresAction, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Paid, PaymentStatus.Processing, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Paid, PaymentStatus.Failed, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Paid, PaymentStatus.Paid, TransitionOutcome.Redundant },
        { PaymentStatus.Paid, PaymentStatus.Canceled, TransitionOutcome.Conflict },
        { PaymentStatus.Paid, PaymentStatus.PartiallyRefunded, TransitionOutcome.Applied },
        { PaymentStatus.Paid, PaymentStatus.Refunded, TransitionOutcome.Applied },

        { PaymentStatus.Canceled, PaymentStatus.Created, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Canceled, PaymentStatus.RequiresAction, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Canceled, PaymentStatus.Processing, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Canceled, PaymentStatus.Failed, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Canceled, PaymentStatus.Paid, TransitionOutcome.Conflict },
        { PaymentStatus.Canceled, PaymentStatus.Canceled, TransitionOutcome.Redundant },
        { PaymentStatus.Canceled, PaymentStatus.PartiallyRefunded, TransitionOutcome.Conflict },
        { PaymentStatus.Canceled, PaymentStatus.Refunded, TransitionOutcome.Conflict },

        { PaymentStatus.PartiallyRefunded, PaymentStatus.Created, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.PartiallyRefunded, PaymentStatus.RequiresAction, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.PartiallyRefunded, PaymentStatus.Processing, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.PartiallyRefunded, PaymentStatus.Failed, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.PartiallyRefunded, PaymentStatus.Paid, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.PartiallyRefunded, PaymentStatus.Canceled, TransitionOutcome.Conflict },
        { PaymentStatus.PartiallyRefunded, PaymentStatus.PartiallyRefunded, TransitionOutcome.Redundant },
        { PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded, TransitionOutcome.Applied },

        { PaymentStatus.Refunded, PaymentStatus.Created, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Refunded, PaymentStatus.RequiresAction, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Refunded, PaymentStatus.Processing, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Refunded, PaymentStatus.Failed, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Refunded, PaymentStatus.Paid, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Refunded, PaymentStatus.Canceled, TransitionOutcome.Conflict },
        { PaymentStatus.Refunded, PaymentStatus.PartiallyRefunded, TransitionOutcome.IgnoredOutOfOrder },
        { PaymentStatus.Refunded, PaymentStatus.Refunded, TransitionOutcome.Redundant },
    };

    [Theory]
    [MemberData(nameof(ExpectedOutcomes))]
    public void Decide_returns_the_outcome_of_the_transition_table(
        PaymentStatus current,
        PaymentStatus requested,
        TransitionOutcome expected)
    {
        TransitionOutcome outcome = PaymentTransitions.Decide(current, SameAmountRefunded, requested, SameAmountRefunded);

        Assert.Equal(expected, outcome);
    }

    [Fact]
    public void Transition_table_has_every_pair_of_current_and_requested_status_exactly_once()
    {
        PaymentStatus[] statuses = Enum.GetValues<PaymentStatus>();
        List<(PaymentStatus Current, PaymentStatus Requested)> allPairs = statuses
            .SelectMany(current => statuses.Select(requested => (current, requested)))
            .ToList();
        List<(PaymentStatus Current, PaymentStatus Requested)> tablePairs = ExpectedOutcomes
            .Select(row => ((PaymentStatus)row[0], (PaymentStatus)row[1]))
            .ToList();

        Assert.Equal(allPairs.Count, tablePairs.Count);
        Assert.Equal(allPairs.ToHashSet(), tablePairs.ToHashSet());
    }

    [Fact]
    public void Larger_partial_refund_is_applied()
    {
        TransitionOutcome outcome = PaymentTransitions.Decide(
            PaymentStatus.PartiallyRefunded, 1_000, PaymentStatus.PartiallyRefunded, 2_500);

        Assert.Equal(TransitionOutcome.Applied, outcome);
    }

    [Fact]
    public void Smaller_partial_refund_is_ignored_as_out_of_order()
    {
        TransitionOutcome outcome = PaymentTransitions.Decide(
            PaymentStatus.PartiallyRefunded, 2_500, PaymentStatus.PartiallyRefunded, 1_000);

        Assert.Equal(TransitionOutcome.IgnoredOutOfOrder, outcome);
    }
}

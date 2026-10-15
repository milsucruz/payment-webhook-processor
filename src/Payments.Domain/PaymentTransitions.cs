namespace Payments.Domain;

// Details: docs/code/PaymentTransitions.md
public static class PaymentTransitions
{
    public static TransitionOutcome Decide(
        PaymentStatus current,
        long currentAmountRefunded,
        PaymentStatus requested,
        long requestedAmountRefunded)
    {
        if (IsImpossibleConflict(current, requested))
        {
            return TransitionOutcome.Conflict;
        }

        if (current == PaymentStatus.PartiallyRefunded && requested == PaymentStatus.PartiallyRefunded)
        {
            return CompareRefunds(currentAmountRefunded, requestedAmountRefunded);
        }

        if (current == requested)
        {
            return TransitionOutcome.Redundant;
        }

        if (IsSwitchBetweenRequiresActionAndProcessing(current, requested))
        {
            return TransitionOutcome.Applied;
        }

        return Level(requested) > Level(current)
            ? TransitionOutcome.Applied
            : TransitionOutcome.IgnoredOutOfOrder;
    }

    private static bool IsImpossibleConflict(PaymentStatus current, PaymentStatus requested)
    {
        if (current == PaymentStatus.Canceled)
        {
            return requested is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;
        }

        return requested == PaymentStatus.Canceled
            && current is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;
    }

    private static TransitionOutcome CompareRefunds(long currentAmountRefunded, long requestedAmountRefunded)
    {
        if (requestedAmountRefunded > currentAmountRefunded)
        {
            return TransitionOutcome.Applied;
        }

        return requestedAmountRefunded == currentAmountRefunded
            ? TransitionOutcome.Redundant
            : TransitionOutcome.IgnoredOutOfOrder;
    }

    private static bool IsSwitchBetweenRequiresActionAndProcessing(PaymentStatus current, PaymentStatus requested)
    {
        return (current, requested) is
            (PaymentStatus.RequiresAction, PaymentStatus.Processing) or
            (PaymentStatus.Processing, PaymentStatus.RequiresAction);
    }

    private static int Level(PaymentStatus status)
    {
        return status switch
        {
            PaymentStatus.Created => 0,
            PaymentStatus.RequiresAction => 1,
            PaymentStatus.Processing => 1,
            PaymentStatus.Failed => 2,
            PaymentStatus.Paid => 3,
            PaymentStatus.Canceled => 3,
            PaymentStatus.PartiallyRefunded => 4,
            PaymentStatus.Refunded => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown payment status."),
        };
    }
}

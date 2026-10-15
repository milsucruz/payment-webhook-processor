namespace Payments.Domain;

public enum TransitionOutcome : byte
{
    Applied = 1,
    IgnoredOutOfOrder = 2,
    Redundant = 3,
    Conflict = 4,
}

namespace Payments.Application.Processing;

public enum ProcessStripeEventOutcome
{
    Applied,
    IgnoredOutOfOrder,
    Redundant,
    Duplicate,
    Conflict,
    InvalidEvent,
    ConcurrencyConflict,
    TransientFailure,
}

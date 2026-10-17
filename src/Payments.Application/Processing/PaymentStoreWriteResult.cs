namespace Payments.Application.Processing;

public enum PaymentStoreWriteResult
{
    Saved,
    DuplicateEvent,
    ConcurrencyConflict,
}

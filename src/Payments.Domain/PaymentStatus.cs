namespace Payments.Domain;

public enum PaymentStatus : byte
{
    Created = 1,
    RequiresAction = 2,
    Processing = 3,
    Failed = 4,
    Paid = 5,
    Canceled = 6,
    PartiallyRefunded = 7,
    Refunded = 8,
}

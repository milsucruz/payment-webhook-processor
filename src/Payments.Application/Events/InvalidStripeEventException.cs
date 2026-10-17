namespace Payments.Application.Events;

public sealed class InvalidStripeEventException : Exception
{
    public InvalidStripeEventException(string message)
        : base(message)
    {
    }

    public InvalidStripeEventException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

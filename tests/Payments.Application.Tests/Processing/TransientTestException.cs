namespace Payments.Application.Tests.Processing;

public sealed class TransientTestException : Exception
{
    public TransientTestException()
        : base("Simulated transient database error.")
    {
    }
}

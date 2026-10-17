using Payments.Application.Processing;

namespace Payments.Application.Tests.Processing;

public sealed class TestTransientErrorDetector : ITransientErrorDetector
{
    public bool IsTransient(Exception exception) => exception is TransientTestException;
}

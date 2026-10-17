namespace Payments.Application.Processing;

public interface ITransientErrorDetector
{
    bool IsTransient(Exception exception);
}

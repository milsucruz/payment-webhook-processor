namespace Payments.Application.Webhooks;

public enum ReceiveStripeEventResult
{
    Published,
    InvalidSignature,
    InvalidEventId,
    PublishFailed,
}

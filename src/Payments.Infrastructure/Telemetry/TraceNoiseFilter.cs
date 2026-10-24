using System.Diagnostics;
using OpenTelemetry;

namespace Payments.Infrastructure.Telemetry;

// Details: docs/code/PaymentsTelemetry.md
public sealed class TraceNoiseFilter : BaseProcessor<Activity>
{
    private const string ReceiverSourceName = "Azure.Messaging.ServiceBus.ServiceBusReceiver";

    public override void OnEnd(Activity data)
    {
        bool isPolling = data.Source.Name == ReceiverSourceName && data.ParentSpanId == default;
        bool hasDroppedParent = data.Parent is { Recorded: false };
        if (isPolling || hasDroppedParent)
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }
}

using System.Diagnostics;
using OpenTelemetry;

namespace Payments.Infrastructure.Telemetry;

// Details: docs/code/PaymentsTelemetry.md
public sealed class ServiceBusPollingFilter : BaseProcessor<Activity>
{
    private const string ReceiverSourceName = "Azure.Messaging.ServiceBus.ServiceBusReceiver";

    public override void OnEnd(Activity data)
    {
        if (data.Source.Name == ReceiverSourceName && data.ParentSpanId == default)
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }
}

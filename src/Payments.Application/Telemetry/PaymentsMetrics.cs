using System.Diagnostics.Metrics;
using Payments.Application.Processing;

namespace Payments.Application.Telemetry;

// Details: docs/code/PaymentsMetrics.md
public sealed class PaymentsMetrics
{
    public const string MeterName = "Payments";

    private const string EventTypeTag = "payments.event_type";
    private const string DeadLetterReasonTag = "payments.dead_letter_reason";
    private const string OutcomeTag = "payments.outcome";

    private readonly Counter<long> eventsReceived;
    private readonly Counter<long> eventsDuplicate;
    private readonly Counter<long> eventsOutOfOrder;
    private readonly Counter<long> eventsDeadLettered;
    private readonly Histogram<double> processingDuration;

    public PaymentsMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        this.eventsReceived = meter.CreateCounter<long>("events.received", "{event}", "Stripe events accepted by the webhook and published to the queue.");
        this.eventsDuplicate = meter.CreateCounter<long>("events.duplicate", "{event}", "Deliveries of an event that was already in the inbox.");
        this.eventsOutOfOrder = meter.CreateCounter<long>("events.out_of_order", "{event}", "Events ignored because they would regress the payment.");
        this.eventsDeadLettered = meter.CreateCounter<long>("events.dead_lettered", "{event}", "Events the worker sent to the dead-letter with a permanent error.");
        this.processingDuration = meter.CreateHistogram<double>("event.processing.duration", "s", "Time from the event entering the queue to its commit.");
    }

    public void EventReceived(string eventType) =>
        this.eventsReceived.Add(1, new KeyValuePair<string, object?>(EventTypeTag, eventType));

    public void EventDeadLettered(string reason) =>
        this.eventsDeadLettered.Add(1, new KeyValuePair<string, object?>(DeadLetterReasonTag, reason));

    public void EventProcessed(ProcessStripeEventOutcome outcome, TimeSpan sinceReceived)
    {
        switch (outcome)
        {
            case ProcessStripeEventOutcome.Duplicate:
                this.eventsDuplicate.Add(1);
                break;
            case ProcessStripeEventOutcome.IgnoredOutOfOrder:
                this.eventsOutOfOrder.Add(1);
                this.RecordDuration(outcome, sinceReceived);
                break;
            case ProcessStripeEventOutcome.Applied:
            case ProcessStripeEventOutcome.Redundant:
                this.RecordDuration(outcome, sinceReceived);
                break;
        }
    }

    private void RecordDuration(ProcessStripeEventOutcome outcome, TimeSpan sinceReceived) =>
        this.processingDuration.Record(sinceReceived.TotalSeconds, new KeyValuePair<string, object?>(OutcomeTag, outcome.ToString()));
}

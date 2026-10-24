extern alias api;

using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using OpenTelemetry.Trace;
using Payments.Application.Processing;
using Payments.Infrastructure.Tests.Webhooks;
using Payments.IntegrationTests.Worker;
using Xunit.Abstractions;

namespace Payments.IntegrationTests.Telemetry;

[Collection(WorkerCollection.Name)]
public class EventTraceTests : IAsyncLifetime
{
    private const string WebhookSecret = "whsec_test_secret";
    private const string ProcessorSource = "Azure.Messaging.ServiceBus.ServiceBusProcessor";
    private const string SenderSource = "Azure.Messaging.ServiceBus.ServiceBusSender";
    private const string ReceiverSource = "Azure.Messaging.ServiceBus.ServiceBusReceiver";
    private static readonly TimeSpan waitTimeout = TimeSpan.FromSeconds(150);

    private readonly WorkerFixture fixture;
    private readonly ITestOutputHelper output;
    private readonly List<Activity> spans = [];

    public EventTraceTests(WorkerFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    public Task InitializeAsync() => this.fixture.PurgeAsync(CancellationToken.None);

    public Task DisposeAsync() => this.fixture.StopWorkersAsync(CancellationToken.None);

    [Fact]
    public async Task Webhook_trace_has_the_six_spans_from_the_api_request_to_the_hub_send()
    {
        const string EventId = "evt_trace_succeeded";
        await using WebApplicationFactory<api::Program> app = this.CreateApi();
        using HttpClient client = app.CreateClient();
        await this.fixture.StartWorkerAsync(loggerProvider: null, faultInjection: false, overrideServices: null, CancellationToken.None);
        string body = Event(EventId, "pi_trace");
        using HttpRequestMessage request = new(HttpMethod.Post, "/webhooks/stripe") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", StripeSignatureHelper.Header(body, WebhookSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Activity webhook = await this.WaitForSpanAsync(span => span.Kind == ActivityKind.Server && Equals(span.GetTagItem("http.route"), "webhooks/stripe"));
        ActivityTraceId traceId = webhook.TraceId;
        Activity notificationConsumed = await this.WaitForSpanAsync(span => span.TraceId == traceId && span.Source.Name == ProcessorSource && HasDestination(span, WorkerFixture.NotificationsQueueName));
        Activity[] trace = this.Snapshot().Where(span => span.TraceId == traceId).ToArray();
        this.WriteTrace(trace);

        Activity eventSent = Assert.Single(trace, span => span.Source.Name == SenderSource && HasDestination(span, WorkerFixture.QueueName));
        Activity eventProcessed = Assert.Single(trace, span => span.Source.Name == ProcessorSource && HasDestination(span, WorkerFixture.QueueName));
        Activity transaction = Assert.Single(trace, span => span.Source.Name == ProcessStripeEvent.ActivitySourceName);
        Activity notificationSent = Assert.Single(trace, span => span.Source.Name == SenderSource && HasDestination(span, WorkerFixture.NotificationsQueueName));
        Assert.Equal(eventProcessed.SpanId, transaction.ParentSpanId);
        Assert.Equal(eventProcessed.SpanId, notificationSent.ParentSpanId);
        Assert.Contains(trace, span => span.Kind == ActivityKind.Client && span.ParentSpanId == transaction.SpanId && span.Source.Name.Contains("SqlClient", StringComparison.Ordinal));
        Assert.True(webhook.StartTimeUtc <= eventSent.StartTimeUtc);
        Assert.True(eventProcessed.StartTimeUtc < notificationConsumed.StartTimeUtc);
        Assert.DoesNotContain(this.Snapshot(), span => span.Source.Name == ReceiverSource && span.ParentSpanId == default);
        await using Payments.Infrastructure.Persistence.PaymentsDbContext dbContext = this.fixture.CreateDbContext();
        Assert.Equal(traceId.ToHexString(), dbContext.InboxEvents.Single(inboxEvent => inboxEvent.EventId == EventId).TraceId);
    }

    private static bool HasDestination(Activity span, string queueName) =>
        span.TagObjects.Any(tag => tag.Key.Contains("destination", StringComparison.Ordinal) && Equals(tag.Value, queueName));

    private static string Event(string eventId, string paymentIntentId) =>
        $$"""
        {"id":"{{eventId}}","type":"payment_intent.succeeded","created":1792181099,
         "data":{"object":{"id":"{{paymentIntentId}}","object":"payment_intent","amount":2000,"currency":"usd","status":"succeeded","metadata":{} } } }
        """;

    private async Task<Activity> WaitForSpanAsync(Func<Activity, bool> predicate)
    {
        using CancellationTokenSource timeout = new(waitTimeout);
        while (true)
        {
            Activity? span = this.Snapshot().FirstOrDefault(predicate);
            if (span is not null)
            {
                return span;
            }

            if (timeout.IsCancellationRequested)
            {
                this.WriteTrace(this.Snapshot());
                throw new TimeoutException("The expected span was not exported.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        }
    }

    private Activity[] Snapshot()
    {
        lock (this.spans)
        {
            return this.spans.ToArray();
        }
    }

    private void WriteTrace(IEnumerable<Activity> trace)
    {
        foreach (Activity span in trace.OrderBy(span => span.StartTimeUtc))
        {
            string tags = string.Join(", ", span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
            this.output.WriteLine($"{span.TraceId} {span.SpanId} <- {span.ParentSpanId} [{span.Source.Name}] {span.Kind} {span.DisplayName} {{{tags}}}");
        }
    }

    private WebApplicationFactory<api::Program> CreateApi() =>
        new WebApplicationFactory<api::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);
            builder.UseSetting("ServiceBus:ConnectionString", this.fixture.ServiceBusConnectionString);
            builder.UseSetting("ServiceBus:EventsQueueName", WorkerFixture.QueueName);
            builder.UseSetting("ServiceBus:NotificationsQueueName", WorkerFixture.NotificationsQueueName);
            builder.UseSetting("ConnectionStrings:Payments", this.fixture.PaymentsConnectionString);
            builder.UseSetting("Admin:ApiKey", "test-admin-key");
            builder.ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(new SynchronizedList(this.spans))));
        });

    private sealed class SynchronizedList : ICollection<Activity>
    {
        private readonly List<Activity> inner;

        public SynchronizedList(List<Activity> inner)
        {
            this.inner = inner;
        }

        public int Count
        {
            get
            {
                lock (this.inner)
                {
                    return this.inner.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public void Add(Activity item)
        {
            lock (this.inner)
            {
                this.inner.Add(item);
            }
        }

        public void Clear()
        {
            lock (this.inner)
            {
                this.inner.Clear();
            }
        }

        public bool Contains(Activity item)
        {
            lock (this.inner)
            {
                return this.inner.Contains(item);
            }
        }

        public void CopyTo(Activity[] array, int arrayIndex)
        {
            lock (this.inner)
            {
                this.inner.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(Activity item)
        {
            lock (this.inner)
            {
                return this.inner.Remove(item);
            }
        }

        public IEnumerator<Activity> GetEnumerator() => this.Snapshot().AsEnumerable().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();

        private Activity[] Snapshot()
        {
            lock (this.inner)
            {
                return this.inner.ToArray();
            }
        }
    }
}

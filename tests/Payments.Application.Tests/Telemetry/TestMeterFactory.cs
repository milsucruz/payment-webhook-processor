using System.Diagnostics.Metrics;

namespace Payments.Application.Tests.Telemetry;

public sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> meters = [];

    public Meter Create(MeterOptions options)
    {
        Meter meter = new(options.Name, options.Version, options.Tags, this);
        this.meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (Meter meter in this.meters)
        {
            meter.Dispose();
        }

        this.meters.Clear();
    }
}

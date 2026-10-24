using System.Runtime.CompilerServices;

namespace Payments.IntegrationTests.Telemetry;

internal static class ServiceBusTracingSwitch
{
    [ModuleInitializer]
    internal static void Enable() => AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
}

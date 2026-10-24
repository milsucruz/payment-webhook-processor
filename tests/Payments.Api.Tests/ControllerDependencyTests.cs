using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Payments.Api.Notifications;
using Payments.Api.Queues;

namespace Payments.Api.Tests;

public class ControllerDependencyTests
{
    [Fact]
    public void Controllers_and_background_services_do_not_depend_on_infrastructure_types()
    {
        List<Type> adapters = typeof(Program).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(ControllerBase)) || type.IsSubclassOf(typeof(BackgroundService)))
            .ToList();

        List<string> forbidden = adapters
            .SelectMany(adapter => adapter.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Concat(adapter.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => field.FieldType))
                .Where(type => type.Assembly.GetName().Name == "Payments.Infrastructure")
                .Select(type => $"{adapter.Name} -> {type.FullName}"))
            .ToList();

        Assert.Contains(adapters, adapter => adapter.IsSubclassOf(typeof(ControllerBase)));
        Assert.Contains(typeof(PaymentNotificationsListener), adapters);
        Assert.Contains(typeof(QueueStatsBroadcaster), adapters);
        Assert.Empty(forbidden);
    }
}

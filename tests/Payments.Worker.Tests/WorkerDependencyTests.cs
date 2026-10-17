using System.Reflection;
using System.Runtime.CompilerServices;

namespace Payments.Worker.Tests;

public class WorkerDependencyTests
{
    [Fact]
    public void Worker_types_other_than_program_do_not_depend_on_infrastructure_types()
    {
        List<Type> workerTypes = typeof(PaymentEventsWorker).Assembly.GetTypes()
            .Where(type => type.Name != "Program" && !type.IsDefined(typeof(CompilerGeneratedAttribute)))
            .ToList();

        List<string> forbidden = workerTypes
            .SelectMany(workerType => workerType.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Concat(workerType.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => field.FieldType))
                .Where(type => type.Assembly.GetName().Name == "Payments.Infrastructure")
                .Select(type => $"{workerType.Name} -> {type.FullName}"))
            .ToList();

        Assert.Contains(typeof(PaymentEventsWorker), workerTypes);
        Assert.Empty(forbidden);
    }
}

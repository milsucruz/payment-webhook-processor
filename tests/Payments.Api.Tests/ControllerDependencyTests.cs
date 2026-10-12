using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Payments.Api.Tests;

public class ControllerDependencyTests
{
    [Fact]
    public void Controllers_do_not_depend_on_infrastructure_types()
    {
        List<Type> controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(ControllerBase)))
            .ToList();

        List<string> forbidden = controllers
            .SelectMany(controller => controller.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Concat(controller.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => field.FieldType))
                .Where(type => type.Assembly.GetName().Name == "Payments.Infrastructure")
                .Select(type => $"{controller.Name} -> {type.FullName}"))
            .ToList();

        Assert.NotEmpty(controllers);
        Assert.Empty(forbidden);
    }
}

using System.Reflection;

namespace Payments.Domain.Tests;

public class DomainDependencyTests
{
    [Fact]
    public void Domain_does_not_reference_other_solution_projects_or_azure_packages()
    {
        var domain = Assembly.Load("Payments.Domain");

        var forbidden = domain.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Payments.", StringComparison.Ordinal)
                || name.StartsWith("Azure.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }
}

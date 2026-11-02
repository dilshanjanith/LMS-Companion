using System.Reflection;

namespace LmsCompanion.UnitTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Application_layer_does_not_depend_on_infrastructure()
    {
        var applicationAssembly = Assembly.Load("LmsCompanion.Application");

        Assert.DoesNotContain(
            applicationAssembly.GetReferencedAssemblies(),
            reference => reference.Name == "LmsCompanion.Infrastructure");
    }
}

using System.Reflection;

namespace Hoshi.Ogs.Tests;

// Placeholder so the test project runs in CI. Real tests arrive with the phase that implements Hoshi.Ogs.
public sealed class AssemblySmokeTests
{
    [Fact]
    public void Assembly_under_test_loads()
    {
        Assembly.Load("Hoshi.Ogs").GetName().Name.Should().Be("Hoshi.Ogs");
    }
}

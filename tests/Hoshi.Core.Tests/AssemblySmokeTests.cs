using System.Reflection;

namespace Hoshi.Core.Tests;

// Placeholder so the test project runs in CI. Real tests arrive with the phase that implements Hoshi.Core.
public sealed class AssemblySmokeTests
{
    [Fact]
    public void Assembly_under_test_loads()
    {
        Assembly.Load("Hoshi.Core").GetName().Name.Should().Be("Hoshi.Core");
    }
}

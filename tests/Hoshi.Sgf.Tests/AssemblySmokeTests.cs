using System.Reflection;

namespace Hoshi.Sgf.Tests;

// Placeholder so the test project runs in CI. Real tests arrive with the phase that implements Hoshi.Sgf.
public sealed class AssemblySmokeTests
{
    [Fact]
    public void Assembly_under_test_loads()
    {
        Assembly.Load("Hoshi.Sgf").GetName().Name.Should().Be("Hoshi.Sgf");
    }
}

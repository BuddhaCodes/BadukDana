using System.Reflection;

namespace Hoshi.App.Tests;

/// <summary>
/// Enforces the dependency rule from CLAUDE.md §3:
/// App → Ogs, Sgf, Core · Ogs → Core · Sgf → Core · Engines → Core · Core → (nothing).
/// </summary>
public sealed class ArchitectureTests
{
    public static TheoryData<string, string[]> AllowedDependencies => new()
    {
        { "Hoshi.Core", [] },
        { "Hoshi.Sgf", ["Hoshi.Core"] },
        { "Hoshi.Ogs", ["Hoshi.Core"] },
        { "Hoshi.Engines", ["Hoshi.Core"] },
        { "Hoshi", ["Hoshi.Core", "Hoshi.Sgf", "Hoshi.Ogs"] },
    };

    [Theory]
    [MemberData(nameof(AllowedDependencies))]
    public void Project_only_references_allowed_Hoshi_assemblies(string assemblyName, string[] allowed)
    {
        Assembly assembly = Assembly.Load(assemblyName);

        string[] hoshiReferences = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n == "Hoshi" || n.StartsWith("Hoshi.", StringComparison.Ordinal))
            .ToArray();

        hoshiReferences.Should().BeSubsetOf(allowed, "{0} must respect the layer rule", assemblyName);
    }

    [Theory]
    [InlineData("Hoshi.Core")]
    [InlineData("Hoshi.Sgf")]
    [InlineData("Hoshi.Ogs")]
    [InlineData("Hoshi.Engines")]
    public void Non_UI_projects_do_not_reference_Avalonia(string assemblyName)
    {
        Assembly assembly = Assembly.Load(assemblyName);

        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Should().NotContain(n => n.StartsWith("Avalonia", StringComparison.Ordinal));
    }
}

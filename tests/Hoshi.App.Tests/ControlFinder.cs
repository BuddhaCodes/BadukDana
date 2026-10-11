using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Hoshi.App.Tests;

/// <summary>Finds a named control anywhere below a root, across name scopes (views inside windows and pages).</summary>
internal static class ControlFinder
{
    public static T? FindDeep<T>(this Visual root, string name)
        where T : Control =>
        (root as ILogical)?.GetSelfAndLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? root.GetSelfAndVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
}

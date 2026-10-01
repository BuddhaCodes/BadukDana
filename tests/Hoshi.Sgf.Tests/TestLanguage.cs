using System.Runtime.CompilerServices;

namespace Hoshi.Sgf.Tests;

/// <summary>Tests assert the Spanish texts (Hoshi's original language); English is checked in LocalizationTests.</summary>
internal static class TestLanguage
{
#pragma warning disable CA2255 // A test assembly is the place where a module initializer is wanted.
    [ModuleInitializer]
    internal static void UseSpanish() => Hoshi.Core.Localization.Tr.SetLanguage(Hoshi.Core.Localization.Tr.Spanish);
#pragma warning restore CA2255
}

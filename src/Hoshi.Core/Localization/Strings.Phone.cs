namespace Hoshi.Core.Localization;

public static partial class Tr
{
    /// <summary>Phones (Android): tap and confirm, the single-view layout, its sheets and pages.</summary>
    static partial void AddPhone(Dictionary<string, (string En, string Es)> d)
    {
        Add(d, "Prefs.ConfirmMoves", "Tap twice to play", "Tocar dos veces para jugar");
        Add(d, "Prefs.ConfirmMovesHint",
            "The first tap shows the stone with guide lines; tapping the same point again plays it. Avoids slips of the finger on small boards.",
            "El primer toque muestra la piedra con líneas guía; tocar otra vez el mismo punto la juega. Evita jugadas por error en tableros pequeños.");
        Add(d, "Phone.Moves", "Moves", "Jugadas");
        Add(d, "Phone.Back", "Back", "Atrás");
        Add(d, "Phone.PlayKataGo", "Play against KataGo…", "Jugar contra KataGo…");
    }
}

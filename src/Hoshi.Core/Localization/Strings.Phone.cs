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
        Add(d, "Phone.Resize", "Drag to resize", "Arrastra para cambiar el tamaño");
        Add(d, "Prefs.Haptics", "Vibrate when a stone is placed", "Vibrar al colocar una piedra");

        // Zen mode.
        Add(d, "Zen.Menu", "Zen mode", "Modo zen");
        Add(d, "Zen.Exit", "Leave zen", "Salir del zen");
        Add(d, "Zen.ExitHint", "Z also leaves zen mode", "Z también sale del modo zen");
        Add(d, "Zen.On", "Zen mode: just the board. Z to leave.", "Modo zen: solo el tablero. Z para salir.");
        Add(d, "Zen.Off", "Zen mode off.", "Modo zen desactivado.");
        Add(d, "Prefs.ZenSilence", "Silent zen mode", "Modo zen silencioso");
        Add(d, "Prefs.ZenSilenceHint", "Zen mode (Z) shows just the board with a still background and subtle effects; silent, it also turns off effects, music and every sound but the stones.", "El modo zen (Z) deja solo el tablero, con el fondo quieto y efectos sutiles; en silencioso también apaga efectos, música y todos los sonidos salvo las piedras.");

        // Board display (desktop and phones).
        Add(d, "Prefs.ShowCoordinates", "Coordinates around the board", "Coordenadas alrededor del tablero");
        Add(d, "Prefs.MoveNumbers", "Move numbers", "Números de jugada");
        Add(d, "Prefs.MoveNumbers.Off", "None", "Ninguno");
        Add(d, "Prefs.MoveNumbers.Recent", "Last 10 moves", "Últimas 10 jugadas");
        Add(d, "Prefs.MoveNumbers.All", "All moves", "Todas las jugadas");
    }
}

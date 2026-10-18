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
        Add(d, "Home.Menu", "Home", "Inicio");
        Add(d, "Phone.AskUndo", "Undo?", "¿Deshacer?");
        Add(d, "Phone.FairPlay", "Fair play", "Juego limpio");
        Add(d, "Phone.FairPlayTip", "No AI help while you play on OGS: analysis, hints and the joseki trainer are off.", "Sin ayuda de IA mientras juegas en OGS: el análisis, las pistas y el entrenador de josekis están apagados.");
        Add(d, "Phone.ConfirmHint", "Tap again, or:", "Toca otra vez, o:");
        Add(d, "Phone.ConfirmMove", "Play {0}", "Confirmar {0}");
        Add(d, "Phone.Winrate", "Win chance", "Probabilidad");
        Add(d, "Phone.BestMove", "Best move", "Mejor jugada");
        Add(d, "Home.Greeting", "What shall we do?", "¿Qué hacemos?");
        Add(d, "Home.Continue", "Continue the game", "Continuar la partida");
        Add(d, "Home.YourGame", "On the board", "En el tablero");
        Add(d, "Home.NewGame", "New game on this device", "Partida nueva en este equipo");
        Add(d, "Home.Modes", "Modes", "Modos");
        Add(d, "Home.Online", "Play on OGS", "Jugar en OGS");
        Add(d, "Home.OnlineDetail", "Automatch, challenges", "Automatch y desafíos");
        Add(d, "Home.KataGo", "Against KataGo", "Contra KataGo");
        Add(d, "Home.KataGoDetail", "A human-like level", "Con nivel humano");
        Add(d, "Home.Problems", "Problems", "Problemas");
        Add(d, "Home.ProblemsDetail", "Life and death, review", "Vida y muerte, repaso");
        Add(d, "Home.Open", "Open SGF", "Abrir SGF");
        Add(d, "Home.OpenDetail", "A game or a lesson", "Una partida o una lección");
        Add(d, "Home.Study", "Study", "Estudiar");
        Add(d, "Home.StudyDetail", "Notes and drawings", "Notas y dibujos");
        Add(d, "Home.Replays", "Review", "Revisar");
        Add(d, "Home.ReplaysDetail", "Your saved games", "Tus partidas guardadas");
        Add(d, "Phone.Back", "Back", "Atrás");
        Add(d, "Phone.PlayKataGo", "Play against KataGo…", "Jugar contra KataGo…");
        Add(d, "Phone.Resize", "Drag to resize", "Arrastra para cambiar el tamaño");
        Add(d, "Prefs.Haptics", "Vibrate when a stone is placed", "Vibrar al colocar una piedra");

        // Counting at the end of a local game.
        Add(d, "Count.Title", "Counting", "Conteo");
        Add(d, "Count.Hint", "Counting: tap a group to mark it dead or alive, then accept.", "Conteo: toca un grupo para marcarlo muerto o vivo y acepta.");
        Add(d, "Count.Wins", "{0} wins by {1}", "Ganan {0} por {1}");
        Add(d, "Count.Draw", "Draw (jigo)", "Empate (jigo)");
        Add(d, "Count.TerritoryLine", "{0}: {1} territory + {2} prisoners + {3} komi = {4}", "{0}: {1} de territorio + {2} prisioneros + {3} de komi = {4}");
        Add(d, "Count.AreaLine", "{0}: {1} stones + {2} territory + {3} komi = {4}", "{0}: {1} piedras + {2} de territorio + {3} de komi = {4}");
        Add(d, "Count.Accept", "Accept result", "Aceptar resultado");
        Add(d, "Count.KeepPlaying", "Keep playing", "Seguir jugando");
        Add(d, "Count.Saved", "Result saved: {0}.", "Resultado guardado: {0}.");
        Add(d, "Count.Menu", "Count the score (end of a local game)", "Contar los puntos (final de partida local)");

        // KataGo's review in the game.
        Add(d, "Analysis.Better", "better {0}", "mejor {0}");
        Add(d, "Review.Menu", "Write KataGo's review into the game", "Escribir la revisión de KataGo en la partida");
        Add(d, "Review.Working", "KataGo is reviewing the game…", "KataGo está revisando la partida…");
        Add(d, "Review.Done", "Review written: {0} moves judged; mistakes are red in the tree.", "Revisión escrita: {0} jugadas valoradas; los errores salen en rojo en el árbol.");
        Add(d, "Review.NoEngine", "KataGo is needed for the review (and is off during your live OGS games).", "La revisión necesita KataGo (y está apagado en tus partidas de OGS en curso).");

        // Your turn in another OGS game.
        Add(d, "Turn.Title", "Your move", "Te toca");
        Add(d, "Turn.Notice", "Your move against {0}", "Te toca contra {0}");
        Add(d, "Turn.Open", "Open", "Abrir");

        // Local clock.
        Add(d, "Clock.Menu", "Clock", "Reloj");
        Add(d, "Clock.Pause", "Pause / resume", "Pausar / seguir");
        Add(d, "Clock.Remove", "No clock", "Sin reloj");
        Add(d, "Clock.Started", "Clock: {0}. It runs for the player to move.", "Reloj: {0}. Corre para quien juega.");
        Add(d, "Clock.Flagged", "{0} ran out of time.", "{0} se quedaron sin tiempo.");

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

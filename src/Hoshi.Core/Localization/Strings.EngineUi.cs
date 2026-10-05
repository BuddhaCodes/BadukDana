namespace Hoshi.Core.Localization;

public static partial class Tr
{
    /// <summary>GTP engines: playing against them, engine vs engine, the GTP console and Preferences → Engines.</summary>
    static partial void AddEngineUi(Dictionary<string, (string En, string Es)> d)
    {
        Add(d, "Main.PlayEngine", "Play against an engine…", "Jugar contra un motor…");
        Add(d, "Main.GtpConsole", "GTP console", "Consola GTP");

        Add(d, "Engines.HoshiKataGo", "Hoshi's KataGo", "KataGo de Hoshi");
        Add(d, "Engines.Starting", "Starting {0}…", "Iniciando {0}…");
        Add(d, "Engines.You", "You", "Tú");
        Add(d, "Engines.MatchTitle", "{0} (Black) vs {1} (White)", "{0} (negras) vs {1} (blancas)");
        Add(d, "Engines.Pause", "Pause", "Pausar");
        Add(d, "Engines.Resume", "Resume", "Continuar");
        Add(d, "Engines.Paused", "Paused", "En pausa");
        Add(d, "Engines.Thinking", "{0} is thinking…", "{0} está pensando…");
        Add(d, "Engines.Resigned", "{0} resigns", "{0} abandona");
        Add(d, "Engines.IllegalMove", "{0} played an illegal move ({1}). Paused.", "{0} jugó una jugada ilegal ({1}). En pausa.");
        Add(d, "Engines.End", "End", "Terminar");
        Add(d, "Engines.EndTip", "Stop playing against the engine; the moves stay on the board", "Dejar de jugar contra el motor; las jugadas quedan en el tablero");
        Add(d, "Engines.NewGameTitle", "Play against an engine", "Jugar contra un motor");
        Add(d, "Engines.Start", "Start", "Empezar");
        Add(d, "Engines.Size", "Board", "Tablero");
        Add(d, "Engines.Handicap", "Handicap", "Hándicap");
        Add(d, "Engines.NoEngines",
            "No engine yet: install KataGo (Preferences → Analysis) or add a GTP engine (Leela Zero, GNU Go, KataGo…) in Preferences → Engines.",
            "Todavía no hay motores: instala KataGo (Preferencias → Análisis) o añade un motor GTP (Leela Zero, GNU Go, KataGo…) en Preferencias → Motores.");
        Add(d, "Engines.ChooseAnEngine", "Choose an engine for Black, White or both.", "Elige un motor para negras, blancas o ambas.");
        Add(d, "Engines.EngineVsEngineHint",
            "The engines play each other; you can pause, go back and try other moves at any time.",
            "Los motores juegan entre sí; puedes pausar, volver atrás y probar otras jugadas cuando quieras.");
        Add(d, "Engines.PlayHint",
            "Undo takes back your move and the engine's reply. Go back to any position and play: the engine answers there.",
            "Deshacer quita tu jugada y la respuesta del motor. Vuelve a cualquier posición y juega: el motor responde ahí.");
        Add(d, "Engines.ConsoleTitle", "GTP console", "Consola GTP");
        Add(d, "Engines.ConsoleWatermark", "GTP command (e.g. showboard, list_commands, kata-analyze 50)", "Comando GTP (p. ej. showboard, list_commands, kata-analyze 50)");
        Add(d, "Engines.Send", "Send", "Enviar");
        Add(d, "Engines.ShowLog", "Show the engines' log (stderr)", "Mostrar el registro de los motores (stderr)");
        Add(d, "Engines.Clear", "Clear", "Limpiar");
        Add(d, "Engines.ConsoleBlocked", "Engines are off during your OGS game in progress (OGS does not allow AI help).", "Los motores están apagados durante tu partida de OGS en curso (OGS no permite ayuda de IA).");
        Add(d, "Engines.EndOfAnswer", "(end of answer)", "(fin de la respuesta)");
        Add(d, "Engines.NoAnalysisResult", "{0} sent no analysis.", "{0} no envió ningún análisis.");

        Add(d, "Effects.Title", "Visual effects", "Efectos visuales");
        Add(d, "Effects.Off", "Off", "Apagados");
        Add(d, "Effects.Subtle", "Subtle", "Suaves");
        Add(d, "Effects.Full", "Full", "Completos");
        Add(d, "Effects.Tip", "Visual effects: {0} (F)", "Efectos visuales: {0} (F)");
        Add(d, "Effects.Status", "Visual effects: {0}", "Efectos visuales: {0}");
        Add(d, "Effects.Hint",
            "Explosions of strong moves, shattering captures and the atari alert. Subtle keeps a small flash, a gentle fade and the sweat drop. F switches at any time.",
            "Explosiones de las buenas jugadas, capturas que se rompen y el aviso de atari. Suaves deja un destello pequeño, un fundido y la gota de sudor. F los cambia en cualquier momento.");

        Add(d, "Prefs.EnginesTab", "Engines", "Motores");
        Add(d, "Prefs.EnginesIntro",
            "GTP engines to play against, to let play each other (Ctrl+G) and for the analysis panel. Hoshi's KataGo is ready when installed; add any other GTP engine with its command line.",
            "Motores GTP para jugar contra ellos, ponerlos a jugar entre sí (Ctrl+G) y para el panel de análisis. El KataGo de Hoshi está listo cuando está instalado; añade cualquier otro motor GTP con su línea de comandos.");
        Add(d, "Prefs.EngineName", "Name", "Nombre");
        Add(d, "Prefs.EngineArguments", "Arguments", "Argumentos");
        Add(d, "Prefs.EngineInit", "Commands at start", "Comandos al iniciar");
        Add(d, "Prefs.EngineInitHint", "One GTP command per line, sent after it starts (e.g. time_settings 0 5 1 for 5 s per move).", "Un comando GTP por línea, enviado al iniciar (p. ej. time_settings 0 5 1 para 5 s por jugada).");
        Add(d, "Prefs.EngineAdd", "Add engine", "Añadir motor");
        Add(d, "Prefs.EngineRemove", "Remove", "Quitar");
        Add(d, "Prefs.EngineBuiltIn", "Built in: uses the KataGo and network from Preferences → Analysis, with {0} (500 visits per move; edit it to change the strength).",
            "Incluido: usa el KataGo y la red de Preferencias → Análisis, con {0} (500 visitas por jugada; edítalo para cambiar la fuerza).");
        Add(d, "Prefs.EngineTest", "Test", "Probar");
        Add(d, "Prefs.EngineTesting", "Starting the engine…", "Iniciando el motor…");
        Add(d, "Prefs.EngineTestOk", "It works: {0}, {1} GTP commands{2}.", "Funciona: {0}, {1} comandos GTP{2}.");
        Add(d, "Prefs.EngineTestAnalysis", ", analysis with {0}", ", análisis con {0}");
        Add(d, "Prefs.EngineTestNoAnalysis", ", without analysis", ", sin análisis");
        Add(d, "Prefs.EngineNew", "New engine", "Motor nuevo");
        Add(d, "Prefs.EngineNameTaken", "Another engine already has that name.", "Ya hay otro motor con ese nombre.");
        Add(d, "Prefs.EngineListSaved", "Saved.", "Guardado.");
        Add(d, "Prefs.EngineExamples",
            "Examples — KataGo: gtp -model <network.bin.gz> -config <gtp.cfg> · Leela Zero: --gtp -w <network.gz> · GNU Go: --mode gtp · Pachi: (no arguments). Paths with spaces go in quotes.",
            "Ejemplos — KataGo: gtp -model <red.bin.gz> -config <gtp.cfg> · Leela Zero: --gtp -w <red.gz> · GNU Go: --mode gtp · Pachi: (sin argumentos). Las rutas con espacios van entre comillas.");
        Add(d, "Prefs.AnalysisEngine", "Analysis panel", "Panel de análisis");
        Add(d, "Prefs.AnalysisBuiltIn", "Hoshi's KataGo (analysis engine)", "KataGo de Hoshi (motor de análisis)");
        Add(d, "Prefs.AnalysisEngineHint",
            "Engines with lz-analyze or kata-analyze can analyse instead of Hoshi's KataGo. Territory and score need KataGo.",
            "Los motores con lz-analyze o kata-analyze pueden analizar en lugar del KataGo de Hoshi. El territorio y la puntuación necesitan KataGo.");
    }
}

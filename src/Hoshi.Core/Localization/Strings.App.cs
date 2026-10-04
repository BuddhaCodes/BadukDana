namespace Hoshi.Core.Localization;

public static partial class Tr
{
    static partial void AddApp(Dictionary<string, (string En, string Es)> d)
    {
        // Short colour letters and move notation
        Add(d, "Game.BlackShort", "B", "N");
        Add(d, "Game.WhiteShort", "W", "B");
        Add(d, "Game.PassNoun", "pass", "pase");

        // Analysis
        Add(d, "Analysis.EnginePrefix", "KataGo is ", "KataGo está ");
        Add(d, "Analysis.WakingUp", "Waking up KataGo…", "Despertando a KataGo…");
        Add(d, "Analysis.BlockedOnline", "Analysis is off while you play on OGS (its rules forbid AI assistance).", "Análisis desactivado mientras juegas en OGS (sus normas no permiten ayuda de IA).");
        Add(d, "Analysis.LiveVisits", "Analyzing live · {0} visits", "Analizando en vivo · {0} visitas");
        Add(d, "Analysis.Analyzing", "Analyzing…", "Analizando…");
        Add(d, "Analysis.WinrateBlack", "Black {0:0} %", "Negras {0:0} %");
        Add(d, "Analysis.WinrateWhite", "White {0:0} %", "Blancas {0:0} %");
        Add(d, "Analysis.Best", "Best: {0} · {1}", "Mejor: {0} · {1}");
        Add(d, "Analysis.NextSuggested", "Suggested next: {0}", "Siguiente sugerida: {0}");
        Add(d, "Analysis.Territory", "Black {0} (+{1:0}) · White {2} (+{3:0}) · {4}", "Negras {0} (+{1:0}) · Blancas {2} (+{3:0}) · {4}");
        Add(d, "Analysis.QuickEstimate", " (quick estimate)", " (estimación rápida)");
        Add(d, "Analysis.BestMove", "the best move", "la mejor jugada");
        Add(d, "Analysis.Excellent", "excellent", "excelente");
        Add(d, "Analysis.Good", "good", "buena");
        Add(d, "Analysis.Inaccuracy", "inaccuracy", "imprecisa");
        Add(d, "Analysis.Mistake", "mistake", "error");
        Add(d, "Analysis.Blunder", "blunder", "error grave");
        Add(d, "Analysis.Even", "even", "igualada");

        // Local game
        Add(d, "Game.MoveNumber", "Move {0}", "Jugada {0}");
        Add(d, "Game.Captures", "Captures ● {0} · ○ {1}", "Capturas ● {0} · ○ {1}");
        Add(d, "Game.NewGame", "New game", "Nueva partida");
        Add(d, "Game.Rules", "{0} rules", "reglas {0}");
        Add(d, "Game.Rules.Japanese", "Japanese", "japonesas");
        Add(d, "Game.Rules.Chinese", "Chinese", "chinas");
        Add(d, "Game.Rules.Korean", "Korean", "coreanas");
        Add(d, "Game.Rules.NewZealand", "New Zealand", "neozelandesas");
        Add(d, "Game.Untitled", "Untitled", "Sin título");
        Add(d, "Game.EditMode", "Edit mode · {0}", "Modo edición · {0}");
        Add(d, "Game.OverTwoPasses", "Game over: two passes in a row", "Partida terminada: dos pases seguidos");
        Add(d, "Game.Passed", "{0} passes · {1}", "{0} pasa · {1}");
        Add(d, "Game.BlackToPlayAfterPass", "Black to play", "juegan negras");
        Add(d, "Game.WhiteToPlayAfterPass", "White to play", "juegan blancas");
        Add(d, "Game.BlackToPlay", "Black to play", "Juegan negras");
        Add(d, "Game.WhiteToPlay", "White to play", "Juegan blancas");
        Add(d, "Game.IllegalMove", "Illegal move at {0}: {1}", "Jugada ilegal en {0}: {1}");
        Add(d, "Game.NoSgfGame", "The file contains no SGF game.", "El archivo no contiene ninguna partida SGF.");
        Add(d, "Game.OpenedFirstOf", "Opened the first of {0} games in the file", "Abierta la primera de {0} partidas del archivo");
        Add(d, "Game.OpenedWithWarnings", "File opened with {0} warning(s)", "Archivo abierto con {0} advertencia(s)");
        Add(d, "Game.CouldNotOpenFile", "Could not open the file", "No se pudo abrir el archivo");
        Add(d, "Game.Saved", "Saved {0}", "Guardado {0}");
        Add(d, "Game.DiscardChanges", "The game has unsaved changes. Discard them?", "La partida tiene cambios sin guardar. ¿Descartarlos?");
        Add(d, "Game.Tool.BlackStone", "black stone", "piedra negra");
        Add(d, "Game.Tool.WhiteStone", "white stone", "piedra blanca");
        Add(d, "Game.Tool.Triangle", "triangle", "triángulo");
        Add(d, "Game.Tool.Square", "square", "cuadrado");
        Add(d, "Game.Tool.Circle", "circle", "círculo");
        Add(d, "Game.Tool.Cross", "cross", "cruz");
        Add(d, "Game.Tool.Label", "label", "etiqueta");
        Add(d, "Game.Illegal.Occupied", "point occupied", "punto ocupado");
        Add(d, "Game.Illegal.Suicide", "suicide", "suicidio");
        Add(d, "Game.Illegal.Superko", "superko (repeats a position)", "superko (repite una posición)");
        Add(d, "Game.Illegal.OutOfBounds", "off the board", "fuera del tablero");
        Add(d, "Game.Illegal.Other", "not allowed", "no permitida");

        // Dialogs
        Add(d, "Dialog.UnsavedChanges", "Unsaved changes", "Cambios sin guardar");

        // OGS lobby
        Add(d, "Lobby.YourTurn", "Your turn", "Tu turno");
        Add(d, "Lobby.TheirTurn", "Their turn", "Turno rival");
        Add(d, "Lobby.Scoring", "Scoring", "Conteo");
        Add(d, "Lobby.Ranked", "ranked", "clasificatoria");
        Add(d, "Lobby.TestServer", "{0} (test)", "{0} (pruebas)");
        Add(d, "Lobby.Time.Blitz", "Blitz · 30 s + 5×10 s", "Blitz · 30 s + 5×10 s");
        Add(d, "Lobby.Time.Live", "Live · 10 min + 5×30 s", "En vivo · 10 min + 5×30 s");
        Add(d, "Lobby.Time.LiveFischer", "Live · Fischer 5 min + 10 s", "En vivo · Fischer 5 min + 10 s");
        Add(d, "Lobby.Time.Correspondence", "Correspondence · 3 days + 1 day", "Correspondencia · 3 días + 1 día");
        Add(d, "Lobby.Color.Automatic", "Automatic", "Automático");
        Add(d, "Lobby.Color.Random", "Random", "Aleatorio");
        Add(d, "Lobby.Rules.Japanese", "Japanese", "Japonesas");
        Add(d, "Lobby.Rules.Chinese", "Chinese", "Chinas");
        Add(d, "Lobby.Rules.Korean", "Korean", "Coreanas");
        Add(d, "Lobby.AuthorizeInBrowser", "Authorize Hoshi in the browser window…", "Autoriza a Hoshi en la ventana del navegador…");
        Add(d, "Lobby.NoBrowserServer", "No server supports browser sign-in.", "No hay ningún servidor con inicio de sesión por navegador.");
        Add(d, "Lobby.GoogleInBrowser", "Sign in with Google in the browser and authorize Hoshi…", "Entra con Google en el navegador y autoriza a Hoshi…");
        Add(d, "Lobby.SignedOut", "Signed out.", "Sesión cerrada.");
        Add(d, "Lobby.AcceptedGame", "Challenge accepted: game #{0}.", "Desafío aceptado: partida #{0}.");
        Add(d, "Lobby.Accepted", "Challenge accepted.", "Desafío aceptado.");
        Add(d, "Lobby.NoSuchUser", "There is no user «{0}» on {1}.", "No existe el usuario «{0}» en {1}.");
        Add(d, "Lobby.CannotChallengeSelf", "You cannot challenge yourself.", "No puedes desafiarte a ti mismo.");
        Add(d, "Lobby.ChallengeCreated", "Challenge #{0} created. It is now visible on {1}.", "Desafío #{0} creado. Ya es visible en {1}.");
        Add(d, "Lobby.ChallengeCancelled", "Challenge cancelled.", "Desafío cancelado.");
        Add(d, "Lobby.WaitingFor", "Waiting for an opponent for challenge #{0}…", "Esperando rival para el desafío #{0}…");
        Add(d, "Lobby.GameStarted", "Game #{0} started!", "¡Partida #{0} iniciada!");
        Add(d, "Lobby.Connected", "Connected", "Conectado");
        Add(d, "Lobby.Connecting", "Connecting…", "Conectando…");
        Add(d, "Lobby.Reconnecting", "Reconnecting…", "Reconectando…");
        Add(d, "Lobby.ConnectionRejected", "Connection rejected by the server", "Conexión rechazada por el servidor");
        Add(d, "Lobby.Offline", "Offline", "Sin conexión");
        Add(d, "Lobby.CouldNotConnect", "Could not connect to {0}.", "No se pudo conectar con {0}.");
        Add(d, "Lobby.Timeout", "{0} took too long to respond.", "{0} tardó demasiado en responder.");

        // OGS game
        Add(d, "Online.ChatHeader", "{0} · move {1}", "{0} · jugada {1}");
        Add(d, "Online.ChatVariation", "[shared a variation: {0}]", "[compartió una variante: {0}]");
        Add(d, "Online.ChatReview", "[review #{0}]", "[revisión #{0}]");
        Add(d, "Online.ChatSpectator", "spectator", "espectador");
        Add(d, "Online.ChatMuted", "Chat muted. Your messages still go out.", "Chat silenciado. Tus mensajes se siguen enviando.");
        Add(d, "Online.ChatMutedNew", "Chat muted · {0} new message(s).", "Chat silenciado · {0} mensaje(s) nuevo(s).");
        Add(d, "Online.Phrase.Hello", "Hi! Have a good game.", "¡Hola! Buena partida.");
        Add(d, "Online.Phrase.GoodLuck", "Good luck!", "¡Suerte!");
        Add(d, "Online.Phrase.Thanks", "Thanks for the game!", "¡Gracias por la partida!");
        Add(d, "Online.Phrase.WellPlayed", "Well played!", "¡Bien jugado!");
        Add(d, "Online.ConnectingToGame", "Connecting to the game…", "Conectando con la partida…");
        Add(d, "Online.GameOver", "Game over", "Partida terminada");
        Add(d, "Online.StoneRemoval", "Scoring: mark the dead stones and accept", "Conteo: marca las piedras muertas y acepta");
        Add(d, "Online.Sending", "Sending move…", "Enviando jugada…");
        Add(d, "Online.Watching", "Watching", "Observando");
        Add(d, "Online.OpponentTurn", "Opponent's turn", "Turno del rival");
        Add(d, "Online.IllegalMove", "Illegal move at {0}", "Jugada ilegal en {0}");
        Add(d, "Online.NoTimeLimit", "no limit", "sin límite");
        Add(d, "Online.PassOnlyOnTurn", "You can only pass on your turn", "Solo puedes pasar en tu turno");
        Add(d, "Online.Resign", "Resign", "Abandonar");
        Add(d, "Online.ResignConfirm", "Are you sure you want to resign the game?", "¿Seguro que quieres abandonar la partida?");
        Add(d, "Online.UndoRequested", "Undo requested from the opponent", "Deshacer solicitado al rival");
        Add(d, "Online.ScoreAccepted", "Score accepted; waiting for the opponent", "Conteo aceptado; esperando al rival");
        Add(d, "Online.BrowserFailed", "Could not open the browser. Open the OGS authorization page manually.", "No se pudo abrir el navegador. Abre manualmente la página de autorización de OGS.");
        Add(d, "Online.SignOutBeforeSwitch", "Sign out before switching servers.", "Cierra la sesión antes de cambiar de servidor.");
        Add(d, "Online.SignInToOpen", "Sign in to OGS to open the game.", "Inicia sesión en OGS para abrir la partida.");

        // Main window
        Add(d, "Main.DiscardForOnline", "The local game has unsaved changes. Discard them and open the online game?", "La partida local tiene cambios sin guardar. ¿Descartarlos y abrir la partida en línea?");
        Add(d, "Main.CouldNotOpenGame", "Could not open the game", "No se pudo abrir la partida");

        // Dialogs
        Add(d, "Dialog.Yes", "Yes", "Sí");
        Add(d, "Dialog.No", "No", "No");
        Add(d, "Dialog.Ok", "OK", "Aceptar");
        Add(d, "Dialog.OpenSgf", "Open SGF", "Abrir SGF");
        Add(d, "Dialog.SaveSgf", "Save SGF", "Guardar SGF");

        // Preferences
        Add(d, "Prefs.EngineConfigured", "KataGo configured.", "KataGo configurado.");
        Add(d, "Prefs.PickExecutable", "KataGo executable (katago / katago.exe)", "Ejecutable de KataGo (katago / katago.exe)");
        Add(d, "Prefs.PickModel", "KataGo neural network (.bin.gz)", "Red neuronal de KataGo (.bin.gz)");
        Add(d, "Prefs.PickConfig", "Analysis configuration (analysis_example.cfg)", "Configuración de análisis (analysis_example.cfg)");
        Add(d, "Prefs.EngineSaved", "Saved. KataGo will start with the first analysis.", "Guardado. KataGo arrancará con el primer análisis.");
        Add(d, "Prefs.TestingEngine", "Testing KataGo…", "Probando KataGo…");
        Add(d, "Prefs.EngineWorks", "KataGo works: on an empty 9×9 it suggests {0}.", "KataGo funciona: en 9×9 vacío propone {0}.");
        Add(d, "Prefs.EngineTimeout", "KataGo did not answer in time.", "KataGo no respondió a tiempo.");

        // Engine host
        Add(d, "KataGo.Downloading", "Downloading KataGo… {0} %", "Descargando KataGo… {0} %");
        Add(d, "KataGo.Ready", "KataGo is ready. It warms up in the background.", "KataGo está listo. Se prepara en segundo plano.");
        Add(d, "KataGo.StillMissing", "KataGo was downloaded but could not be used; see the log.", "KataGo se descargó pero no se pudo usar; mira el registro.");
        Add(d, "KataGo.Failed", "Could not install KataGo: {0}", "No se pudo instalar KataGo: {0}");
        Add(d, "KataGo.Damaged", "The download is damaged (its checksum does not match). Please try again.", "La descarga está dañada (su suma de verificación no coincide). Prueba otra vez.");
        Add(d, "KataGo.NoBuild", "KataGo has no official build for this system. On a Mac, install it with Homebrew (brew install katago) and restart Hoshi: it will find it and download the network.", "KataGo no tiene versión oficial para este sistema. En un Mac, instálalo con Homebrew (brew install katago) y reinicia Hoshi: lo encontrará y descargará la red.");
        Add(d, "Engine.NotConfigured", "KataGo is not installed (install it from the card on the board or ☰ → Preferences → Analysis).", "KataGo no está instalado (instálalo desde el aviso del tablero o ☰ → Preferencias → Análisis).");
        Add(d, "Engine.Unavailable", "KataGo is not available.", "KataGo no está disponible.");

        // Music
        Add(d, "Music.NotOnMac", "Adaptive music is not available on macOS yet.", "La música adaptativa aún no está disponible en macOS.");
        Add(d, "Music.NoAudioOutput", "Could not open the audio output: {0}", "No se pudo abrir la salida de audio: {0}");
        Add(d, "Music.Stopped", "The music stopped: {0}", "La música se detuvo: {0}");

        // Themes
        Add(d, "Theme.Night.Name", "Night sky", "Cielo nocturno");
        Add(d, "Theme.Night.Description", "Hoshi means “star”: a midnight-blue sky with twinkling stars and pale gold.", "Hoshi significa «estrella»: cielo azul noche con estrellas que titilan y oro pálido.");
        Add(d, "Theme.Sumi.Name", "Ink and gold", "Tinta y oro");
        Add(d, "Theme.Sumi.Description", "Sumi-e nocturne: slowly drifting ink mist, gold and seal red.", "Nocturno sumi-e: niebla de tinta que se mueve despacio, oro y rojo sello.");
        Add(d, "Theme.Zen.Name", "Zen garden", "Jardín zen");
        Add(d, "Theme.Zen.Description", "Raked sand that shifts very slowly, stone tones and moss green.", "Arena rastrillada que se mueve muy despacio, tonos piedra y verde musgo.");
        Add(d, "Theme.Minimal.Name", "Warm minimal", "Minimal cálido");
        Add(d, "Theme.Minimal.Description", "Understated: washi paper, light wood and terracotta, with micro-animations.", "Sobrio: papel washi, madera clara y terracota, con micro-animaciones.");
        Add(d, "Theme.Classic.Name", "Classic", "Clásico");
        Add(d, "Skin.FromTheme", "As in the theme", "Como en el tema");
        Add(d, "Skin.FromThemeNamed", "Theme: {0}", "Del tema: {0}");
        Add(d, "Skin.Board.KayaMasame", "Kaya, straight grain", "Kaya, veta recta (masame)");
        Add(d, "Skin.Board.KayaItame", "Kaya, flat grain", "Kaya, veta en arco (itame)");
        Add(d, "Skin.Board.ShinKaya", "Shin-kaya (pale spruce)", "Shin-kaya (pícea clara)");
        Add(d, "Skin.Board.Katsura", "Katsura", "Katsura");
        Add(d, "Skin.Board.Bamboo", "Bamboo", "Bambú");
        Add(d, "Skin.Board.Walnut", "Walnut (dark)", "Nogal (oscuro)");
        Add(d, "Skin.Board.Sabaki", "Sabaki (Shudan)", "Sabaki (Shudan)");
        Add(d, "Skin.Board.Kaya", "Kaya, tinted by the theme", "Kaya teñida por el tema");
        Add(d, "Skin.Stones.ClamSlate", "Clam shell and slate", "Concha y pizarra");
        Add(d, "Skin.Stones.Yunzi", "Yunzi", "Yunzi");
        Add(d, "Skin.Stones.Glass", "Glass", "Cristal");
        Add(d, "Skin.Stones.Ceramic", "Ceramic (matte)", "Cerámica (mate)");
        Add(d, "Skin.Stones.Jade", "Jade and obsidian", "Jade y obsidiana");
        Add(d, "Skin.Stones.Pearl", "Pearl (vector)", "Perla (vectorial)");
        Add(d, "Skin.Stones.Soft", "Soft (vector)", "Suaves (vectoriales)");
        Add(d, "Skin.Stones.Shudan", "Shudan (Sabaki)", "Shudan (Sabaki)");
        Add(d, "Skin.Bg.Night", "Night sky (animated)", "Cielo nocturno (animado)");
        Add(d, "Skin.Bg.Ink", "Ink mist (animated)", "Niebla de tinta (animada)");
        Add(d, "Skin.Bg.Zen", "Zen sand (animated)", "Arena zen (animada)");
        Add(d, "Skin.Bg.Sashiko", "Indigo sashiko", "Sashiko índigo");
        Add(d, "Skin.Bg.Walnut", "Walnut table", "Mesa de nogal");
        Add(d, "Skin.Bg.Slate", "Slate", "Pizarra");
        Add(d, "Skin.Bg.Linen", "Charcoal linen", "Lino carbón");
        Add(d, "Skin.Bg.Sudare", "Bamboo blind", "Persiana de bambú (sudare)");
        Add(d, "Skin.Bg.Washi", "Washi paper", "Papel washi");
        Add(d, "Skin.Bg.Tatami", "Tatami", "Tatami");
        Add(d, "Theme.Classic.Description", "A tribute to Sabaki: tatami, Shudan wood and a dark grey interface.", "Homenaje a Sabaki: tatami, madera de Shudan e interfaz gris oscura.");
    }
}

namespace Hoshi.Core.Localization;

public static partial class Tr
{
    /// <summary>Searching the game library.</summary>
    static partial void AddLibrary(Dictionary<string, (string En, string Es)> d)
    {
        Add(d, "Access.Move", "Move {0}: {1} {2}.", "Jugada {0}: {1} {2}.");
        Add(d, "Access.Start", "Start of the game.", "Inicio de la partida.");
        Add(d, "Access.ToPlay", "{0} to play.", "Juegan {0}.");
        Add(d, "Access.Over", "The game is over.", "La partida ha terminado.");
        Add(d, "Access.Captures", "Captures: black {0}, white {1}.", "Capturas: negras {0}, blancas {1}.");
        Add(d, "Access.NotACoordinate", "\"{0}\" is not a point of the board (e.g. D4, Q16 or pass).", "«{0}» no es un punto del tablero (p. ej. D4, Q16 o pasa).");
        Add(d, "Access.MoveEntry", "Play at… (D4, pass)", "Jugar en… (D4, pasa)");
        Add(d, "Prefs.UiScale", "Interface size", "Tamaño de la interfaz");
        Add(d, "Prefs.MoveEntry", "Play by typing a coordinate", "Jugar escribiendo la coordenada");
        Add(d, "Prefs.MoveEntryHint", "A box under the board: type D4 (or pass) and Enter. With a screen reader the board also reads the last move, who plays and the captures.", "Una casilla bajo el tablero: escribe D4 (o pasa) y Enter. Con un lector de pantalla el tablero también lee la última jugada, a quién le toca y las capturas.");
        Add(d, "Presence.Studying", "Studying a game", "Estudiando una partida");
        Add(d, "Presence.StudyingState", "with Hoshi", "con Hoshi");
        Add(d, "Presence.PlayingLocal", "Playing Go", "Jugando al go");
        Add(d, "Presence.PlayingEngine", "Playing against an engine", "Jugando contra un motor");
        Add(d, "Presence.PlayingOgs", "Playing on OGS", "Jugando en OGS");
        Add(d, "Presence.Problems", "Solving problems", "Resolviendo problemas");
        Add(d, "Prefs.Discord", "Show what I do in Discord", "Mostrar en Discord lo que hago");
        Add(d, "Prefs.DiscordAppId", "Discord application id", "Id de la aplicación de Discord");
        Add(d, "Prefs.DiscordHint", "Only \"Studying a game\" or \"Playing on OGS\" (never the opponent or the game), through the Discord app on this computer. Create an application at discord.com/developers and paste its id here.", "Solo «Estudiando una partida» o «Jugando en OGS» (nunca el rival ni la partida), a través de la app de Discord de este equipo. Crea una aplicación en discord.com/developers y pega aquí su id.");
        Add(d, "Prefs.DiscordBadId", "The application id is a number (copy it from discord.com/developers).", "El id de la aplicación es un número (cópialo de discord.com/developers).");
        Add(d, "Main.MoreTip", "More: territory, joseki hints, online", "Más: territorio, josekis, en línea");
        Add(d, "Pause.Pause", "Pause the game", "Pausar la partida");
        Add(d, "Pause.Resume", "Resume the game", "Reanudar la partida");
        Add(d, "Pause.On", "Game paused: the clock and the engine wait.", "Partida en pausa: el reloj y el motor esperan.");
        Add(d, "Pause.Off", "Game resumed.", "Partida reanudada.");
        Add(d, "Pause.Title", "Paused", "En pausa");
        Add(d, "Pause.Tip", "Pause this local game (the clock and the engine). Online games cannot be paused here.", "Pausa esta partida local (el reloj y el motor). Las partidas en línea no se pausan aquí.");
        Add(d, "Clock.LocalOnly", "Clocks are for games on this device; online games use the server's clock.", "El reloj es para partidas en este equipo; las partidas en línea usan el reloj del servidor.");
        Add(d, "Count.MenuTip", "After two passes in a game on this device: mark dead groups with a click, see the score with the game's rules and write the result into the game. Online games are counted on the server.", "Tras dos pases en una partida en este equipo: marca los grupos muertos con un clic, mira el resultado según las reglas y guárdalo en la partida. Las partidas en línea se cuentan en el servidor.");
        Add(d, "Main.PromoteVariationTip", "The shown branch becomes the first line of the game (the one → follows and the game is saved with); nothing is deleted.", "La rama mostrada pasa a ser la primera línea de la partida (la que sigue → y la que cuenta como partida); no se borra nada.");
        Add(d, "Main.DeleteNodeTip", "Removes the shown move with every move and variation after it (it asks first when there is more than one move).", "Quita la jugada mostrada con todas las jugadas y variantes que vienen después (pregunta antes si hay más de una).");
        Add(d, "Game.DeleteConfirm", "Delete {0} moves (this one and everything after it, variations included)?", "¿Borrar {0} jugadas (esta y todo lo que sigue, variantes incluidas)?");
        Add(d, "Game.Deleted", "{0} moves deleted.", "{0} jugadas borradas.");
        Add(d, "Count.NeedPasses", "Counting starts after two passes in a row (P passes).", "El conteo empieza tras dos pases seguidos (P pasa).");
        Add(d, "Study.SectionMove", "This move", "Esta jugada");
        Add(d, "Study.SectionDraw", "Draw", "Dibujar");
        Add(d, "Study.SectionGame", "Game", "Partida");
        Add(d, "Menu.NewGame", "NEW GAME", "NUEVA PARTIDA");
        Add(d, "Menu.Study", "STUDY AND PRACTISE", "ESTUDIAR Y PRACTICAR");
        Add(d, "Menu.ThisGame", "THIS GAME", "ESTA PARTIDA");
        Add(d, "Menu.Settings", "SETTINGS", "AJUSTES");
        Add(d, "Replays.Player", "Player", "Jugador");
        Add(d, "Replays.From", "From", "Desde");
        Add(d, "Replays.To", "To", "Hasta");
        Add(d, "Replays.AnyResult", "Any result", "Cualquier resultado");
        Add(d, "Replays.BlackWins", "Black won", "Ganaron negras");
        Add(d, "Replays.WhiteWins", "White won", "Ganaron blancas");
        Add(d, "Replays.UsePattern", "Search the board's pattern", "Buscar el patrón del tablero");
        Add(d, "Replays.UsePatternTip", "The stones in the shown move's study area (draw one with the study's area tool), else the corner of the last move. Found in any corner, with either colours.", "Las piedras del área de estudio de la jugada mostrada (dibuja una con la herramienta de área del estudio) o, si no hay, la esquina de la última jugada. Se encuentra en cualquier esquina y con cualquier color.");
        Add(d, "Replays.PatternActive", "Pattern {0}×{1} with {2} stones", "Patrón {0}×{1} con {2} piedras");
        Add(d, "Replays.PatternAt", "pattern at move {0}", "patrón en la jugada {0}");
        Add(d, "Replays.ClearPattern", "Remove pattern", "Quitar patrón");
        Add(d, "Replays.ClearFilters", "Clear", "Limpiar");
        Add(d, "Replays.Searching", "Searching the games…", "Buscando en las partidas…");
        Add(d, "Replays.NoMatch", "No game matches.", "Ninguna partida coincide.");
        Add(d, "Replays.NoPattern", "No stones for a pattern: draw a study area around them or play a move.", "No hay piedras para un patrón: dibuja un área de estudio a su alrededor o juega una jugada.");
    }
}

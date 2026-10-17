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

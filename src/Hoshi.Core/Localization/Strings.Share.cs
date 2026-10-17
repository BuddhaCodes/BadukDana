namespace Hoshi.Core.Localization;

public static partial class Tr
{
    /// <summary>Sharing and exporting (images, animations, diagrams, the web viewer).</summary>
    static partial void AddShare(Dictionary<string, (string En, string Es)> d)
    {
        Add(d, "Share.Menu", "Share", "Compartir");
        Add(d, "Share.SaveTitle", "Export", "Exportar");
        Add(d, "Share.TypePng", "PNG image", "Imagen PNG");
        Add(d, "Share.TypeGif", "Animated GIF", "GIF animado");
        Add(d, "Share.TypeText", "Text", "Texto");
        Add(d, "Share.Image", "Board as an image (PNG)…", "Tablero como imagen (PNG)…");
        Add(d, "Share.Animation", "Game as an animation (GIF)…", "Partida como animación (GIF)…");
        Add(d, "Share.AnimationTip", "The line through the shown move, from the start to its end. Captures and KataGo's blunders stay longer.", "La línea de la jugada mostrada, del inicio al final. Las capturas y los errores graves de KataGo duran más.");
        Add(d, "Share.ImageSuffix", "board", "tablero");
        Add(d, "Share.AnimationSuffix", "animation", "animación");
        Add(d, "Share.Rendering", "Drawing the animation…", "Dibujando la animación…");
        Add(d, "Share.Saved", "Saved {0}.", "Guardado {0}.");
        Add(d, "Share.ShareImage", "Share the board…", "Compartir el tablero…");
        Add(d, "Share.ShareAnimation", "Share the game as an animation…", "Compartir la partida como animación…");
        Add(d, "Share.CopySgf", "Copy the game (SGF)", "Copiar la partida (SGF)");
        Add(d, "Share.ShareSgf", "Share the game (SGF)…", "Compartir la partida (SGF)…");
        Add(d, "Share.PasteSgf", "Open from the clipboard (SGF or OGS link)", "Abrir desde el portapapeles (SGF o enlace de OGS)");
        Add(d, "Share.CopyDiagram", "Copy as a diagram (Sensei's Library)", "Copiar como diagrama (Sensei's Library)");
        Add(d, "Share.CopyCodeBlock", "Copy as a text board (Discord, Reddit)", "Copiar como tablero de texto (Discord, Reddit)");
        Add(d, "Share.DiagramTitle", "Move {0}", "Jugada {0}");
        Add(d, "Share.SgfCopied", "Game copied as SGF.", "Partida copiada como SGF.");
        Add(d, "Share.DiagramCopied", "Diagram copied.", "Diagrama copiado.");
        Add(d, "Share.Clipboard", "the clipboard", "el portapapeles");
        Add(d, "Share.NothingToPaste", "The clipboard has no SGF game or OGS link.", "El portapapeles no tiene una partida SGF ni un enlace de OGS.");
        Add(d, "Share.WebViewer", "Game as a web page (HTML)…", "Partida como página web (HTML)…");
        Add(d, "Share.WebViewerTip", "One file that opens in any browser, with the variations, the comments and the study notes.", "Un único archivo que se abre en cualquier navegador, con las variantes, los comentarios y las notas del estudio.");
        Add(d, "Share.ViewerSuffix", "viewer", "visor");
        Add(d, "Share.HideNames", "Hide the players' names", "Ocultar los nombres de los jugadores");
        Add(d, "Share.ShowBrand", "Show \"Made with Hoshi\"", "Mostrar «Hecho con Hoshi»");
        Add(d, "Viewer.Board", "Go board", "Tablero de go");
        Add(d, "Viewer.Move", "Move", "Jugada");
        Add(d, "Viewer.Pass", "pass", "pasa");
        Add(d, "Viewer.Captures", "Captures", "Capturas");
        Add(d, "Viewer.Variations", "Variations", "Variantes");
        Add(d, "Viewer.First", "First move", "Primera jugada");
        Add(d, "Viewer.Previous", "Previous move", "Jugada anterior");
        Add(d, "Viewer.Next", "Next move", "Jugada siguiente");
        Add(d, "Viewer.Last", "Last move", "Última jugada");
        Add(d, "Viewer.MadeWith", "Made with Hoshi", "Hecho con Hoshi");
        Add(d, "Share.Card", "Card of this moment", "Tarjeta de este momento");
        Add(d, "Share.CardTip", "The board with the players, the move, KataGo's verdict and the study note.", "El tablero con los jugadores, la jugada, el veredicto de KataGo y la nota del estudio.");
        Add(d, "Share.CardSquare", "Square (Instagram)…", "Cuadrada (Instagram)…");
        Add(d, "Share.CardWide", "Wide 16:9 (X, Discord)…", "Panorámica 16:9 (X, Discord)…");
        Add(d, "Share.CardStory", "Vertical (stories)…", "Vertical (historias)…");
        Add(d, "Share.CardSuffix", "card", "tarjeta");
        Add(d, "Share.Verdict", "KataGo: {0}", "KataGo: {0}");
        Add(d, "Share.Problem", "This move as a problem…", "Esta jugada como problema…");
        Add(d, "Share.ProblemTip", "The position before the shown move; the move is the answer, copied hidden as a spoiler.", "La posición antes de la jugada mostrada; la jugada es la respuesta, copiada oculta como spoiler.");
        Add(d, "Share.ProblemSuffix", "problem", "problema");
        Add(d, "Share.ProblemSubtitle", "Find the best move", "Encuentra la mejor jugada");
        Add(d, "Share.ProblemNeedsMove", "Show the move that answers the problem first.", "Muestra primero la jugada que responde el problema.");
        Add(d, "Share.Spoiler", "{0} Answer: ||{1}|| (Reddit: >!{1}!<)", "{0} Respuesta: ||{1}|| (Reddit: >!{1}!<)");
        Add(d, "Share.SpoilerCopied", "Problem saved; the answer is on the clipboard as a spoiler.", "Problema guardado; la respuesta está en el portapapeles como spoiler.");
        Add(d, "Share.Summary", "Summary", "Resumen");
        Add(d, "Share.SummaryMonth", "This month…", "Este mes…");
        Add(d, "Share.SummaryYear", "This year…", "Este año…");
        Add(d, "Share.SummaryTip", "Games, results, KataGo's accuracy in the reviewed games, joseki and problems; worked out on this computer.", "Partidas, resultados, precisión según KataGo en las partidas revisadas, josekis y problemas; calculado en este equipo.");
        Add(d, "Share.SummarySuffix", "summary", "resumen");
        Add(d, "Summary.Games", "games ({0} on OGS)", "partidas ({0} en OGS)");
        Add(d, "Summary.WinsLosses", "wins – losses", "victorias – derrotas");
        Add(d, "Summary.Accuracy", "good moves per KataGo ({0} reviewed)", "jugadas buenas según KataGo ({0} revisadas)");
        Add(d, "Summary.BestMoves", "moves that were KataGo's first choice", "jugadas que eran la primera de KataGo");
        Add(d, "Summary.Joseki", "joseki learned ({0} reviewed)", "josekis aprendidos ({0} repasados)");
        Add(d, "Summary.Problems", "problems solved", "problemas resueltos");
        Add(d, "Summary.Of", "{0}'s Go", "El go de {0}");
        Add(d, "Summary.MonthSubtitle", "My month of Go", "Mi mes de go");
        Add(d, "Summary.YearSubtitle", "My year of Go", "Mi año de go");
        Add(d, "Share.SaveFailed", "Could not save: {0}", "No se pudo guardar: {0}");
    }
}

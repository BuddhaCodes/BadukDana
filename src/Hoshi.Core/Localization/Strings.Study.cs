namespace Hoshi.Core.Localization;

public static partial class Tr
{
    /// <summary>The study module: pins, drawings, timeline, sharing, the quiz and the game report.</summary>
    static partial void AddStudy(Dictionary<string, (string En, string Es)> d)
    {
        Add(d, "Main.Study", "Study", "Estudio");
        Add(d, "Main.StudyTip", "Study this game: pins, notes, arrows and \"what if\" lines you can share (S)", "Estudia esta partida: pins, notas, flechas y líneas «¿y si…?» que puedes compartir (S)");
        Add(d, "Main.StudyMenu", "Study panel", "Panel de estudio");
        Add(d, "Dialog.WebPage", "Web page", "Página web");

        Add(d, "Study.Title", "Study", "Estudio");
        Add(d, "Study.Close", "Close the study (S)", "Cerrar el estudio (S)");
        Add(d, "Study.Opened", "Study open: pin moments, draw on the board, reply to notes.", "Estudio abierto: marca momentos, dibuja en el tablero, responde a las notas.");
        Add(d, "Study.Closed", "Study closed (your notes stay in the game).", "Estudio cerrado (tus notas quedan en la partida).");
        Add(d, "Study.Me", "Me", "Yo");
        Add(d, "Study.SignAs", "Sign as", "Firmar como");
        Add(d, "Study.SignAsTip", "Shared studies show who wrote each note", "Los estudios compartidos muestran quién escribió cada nota");
        Add(d, "Study.OnlineNote", "Live game: notes and drawings only, no engine. Drawing tools never send moves.", "Partida en vivo: solo notas y dibujos, sin motor. Las herramientas de dibujo nunca envían jugadas.");

        Add(d, "Study.Tool.Note", "Play", "Jugar");
        Add(d, "Study.Tool.Arrow", "Arrow", "Flecha");
        Add(d, "Study.Tool.Area", "Area", "Zona");
        Add(d, "Study.Tool.Mark", "Circle", "Círculo");
        Add(d, "Study.Tool.Label", "Letter", "Letra");
        Add(d, "Study.Tool.Sequence", "What if", "¿Y si…?");
        Add(d, "Study.Tool.Erase", "Erase", "Borrar");
        Add(d, "Study.Hint.Note", "Clicks play as usual. Pin this move below.", "Los clics juegan como siempre. Marca esta jugada abajo.");
        Add(d, "Study.Hint.Arrow", "Drag from one point to another.", "Arrastra de un punto a otro.");
        Add(d, "Study.Hint.Area", "Drag over the area to highlight.", "Arrastra sobre la zona a resaltar.");
        Add(d, "Study.Hint.Mark", "Click to circle a point; again to remove it.", "Clic para rodear un punto; otra vez para quitarlo.");
        Add(d, "Study.Hint.Label", "Click to add A, B, C…; again to remove it.", "Clic para poner A, B, C…; otra vez para quitarla.");
        Add(d, "Study.Hint.Sequence", "Click to lay out a numbered line without playing it; on a move pinned as a mistake it starts with the better move. Click the last stone to take it back.", "Clic para dibujar una línea numerada sin jugarla; en una jugada marcada como error empieza con la jugada mejor. Clic en la última piedra para quitarla.");
        Add(d, "Study.Hint.Erase", "Click a drawing to remove it.", "Clic en un dibujo para quitarlo.");
        Add(d, "Study.DragToDraw", "Drag on the board to draw.", "Arrastra sobre el tablero para dibujar.");
        Add(d, "Study.NewSequence", "New line", "Nueva línea");
        Add(d, "Study.NewSequenceTip", "Start another \"what if\" line on this move", "Empieza otra línea «¿y si…?» en esta jugada");
        Add(d, "Study.ClearDrawings", "Clear drawings", "Borrar dibujos");
        Add(d, "Study.ClearDrawingsTip", "Remove every drawing on this move", "Quita todos los dibujos de esta jugada");
        Add(d, "Study.Color.Gold", "Gold", "Dorado");
        Add(d, "Study.Color.Red", "Red", "Rojo");
        Add(d, "Study.Color.Blue", "Blue", "Azul");
        Add(d, "Study.Color.Green", "Green", "Verde");
        Add(d, "Study.Color.Purple", "Purple", "Violeta");

        Add(d, "Study.Cat.Mistake", "Mistake", "Error");
        Add(d, "Study.Cat.GoodMove", "Good move", "Buena jugada");
        Add(d, "Study.Cat.Question", "Question", "Pregunta");
        Add(d, "Study.Cat.KeyMoment", "Key moment", "Momento clave");
        Add(d, "Study.Cat.Idea", "Idea", "Idea");
        Add(d, "Study.Cat.Joseki", "Joseki", "Joseki");
        Add(d, "Study.Cat.LifeAndDeath", "Life & death", "Vida y muerte");
        Add(d, "Study.Cat.Time", "Time", "Tiempo");
        Add(d, "Study.Cat.Lesson", "Lesson", "Lección");
        Add(d, "Study.AllCategories", "All pins", "Todos los pins");

        Add(d, "Study.Timeline", "Pins in the game", "Pins de la partida");
        Add(d, "Study.TimelineEmpty", "No pins yet.", "Todavía no hay pins.");
        Add(d, "Study.TimelineCount", "{0} studied moves", "{0} jugadas estudiadas");
        Add(d, "Study.PrevPin", "Previous pin", "Pin anterior");
        Add(d, "Study.NextPin", "Next pin", "Pin siguiente");
        Add(d, "Study.NoNextPin", "No more pins ahead.", "No hay más pins adelante.");
        Add(d, "Study.NoPreviousPin", "No pins before this move.", "No hay pins antes de esta jugada.");
        Add(d, "Study.DrawingsOnly", "drawings", "dibujos");
        Add(d, "Study.NoPins", "No notes on this move.", "No hay notas en esta jugada.");
        Add(d, "Study.NewPin", "Pin this move", "Marcar esta jugada");
        Add(d, "Study.NoteWatermark", "What happened here? (optional)", "¿Qué pasó aquí? (opcional)");
        Add(d, "Study.AddPin", "Pin", "Marcar");
        Add(d, "Study.PinAdded", "Pinned: {0}.", "Marcado: {0}.");
        Add(d, "Study.ReplyWatermark", "Reply…", "Responder…");
        Add(d, "Study.Reply", "Reply", "Responder");
        Add(d, "Study.Delete", "Delete this pin", "Borrar este pin");

        Add(d, "Study.Start", "Start", "Inicio");
        Add(d, "Study.AfterMove", "After move {0}", "Tras la jugada {0}");
        Add(d, "Study.MoveName", "Move {0} · {1} {2}", "Jugada {0} · {1} {2}");

        Add(d, "Study.Share", "Share", "Compartir");
        Add(d, "Study.SaveStudy", "Save study…", "Guardar estudio…");
        Add(d, "Study.SaveStudyTip", "An SGF with the game and every note: send it to a friend or a teacher; Hoshi adds theirs to yours", "Un SGF con la partida y todas las notas: envíalo a un amigo o a tu profesor; Hoshi suma las suyas a las tuyas");
        Add(d, "Study.OpenStudy", "Open study…", "Abrir estudio…");
        Add(d, "Study.OpenStudyTip", "Adds the notes and replies of a study file to this game", "Añade a esta partida las notas y respuestas de un archivo de estudio");
        Add(d, "Study.FileSuffix", "study", "estudio");
        Add(d, "Study.Saved", "Study saved: {0}", "Estudio guardado: {0}");
        Add(d, "Study.CouldNotSave", "Could not save", "No se pudo guardar");
        Add(d, "Study.Merged", "{0} new notes, drawings and replies from {1}.", "{0} notas, dibujos y respuestas nuevas de {1}.");
        Add(d, "Study.NothingNew", "Nothing new in that study: you already have all of it.", "Nada nuevo en ese estudio: ya lo tienes todo.");
        Add(d, "Study.OtherGameTitle", "Another game", "Otra partida");
        Add(d, "Study.OtherGame", "This study belongs to another game. Open it instead of the game on the board?", "Este estudio es de otra partida. ¿Abrirlo en lugar de la partida del tablero?");
        Add(d, "Study.NotThisGame", "That study belongs to another game.", "Ese estudio es de otra partida.");

        Add(d, "Study.ReportButton", "Report…", "Informe…");
        Add(d, "Study.ReportTip", "A printable page with every pinned moment, the notes and (after the game) KataGo's verdict", "Una página imprimible con cada momento marcado, las notas y (después de la partida) el veredicto de KataGo");
        Add(d, "Study.SaveReport", "Save the game report", "Guardar el informe de la partida");
        Add(d, "Study.ReportSuffix", "report", "informe");
        Add(d, "Study.ReportAnalysing", "KataGo is looking at the pinned moments…", "KataGo está revisando los momentos marcados…");
        Add(d, "Study.ReportSaved", "Report saved: {0}", "Informe guardado: {0}");
        Add(d, "Study.ReportTimeout", "KataGo took too long; try the report again.", "KataGo tardó demasiado; vuelve a intentar el informe.");
        Add(d, "Study.Report", "Game report", "Informe de la partida");
        Add(d, "Study.ReportTitle", "{0} vs {1}", "{0} vs {1}");
        Add(d, "Study.ReportAuthors", "notes by {0}", "notas de {0}");
        Add(d, "Study.ReportEmpty", "No pins yet: open the study (S) and pin the moments that mattered.", "Todavía no hay pins: abre el estudio (S) y marca los momentos importantes.");
        Add(d, "Study.Lessons", "Lessons", "Lecciones");
        Add(d, "Study.ReportBest", "best {0}", "mejor {0}");
        Add(d, "Study.ReportDrawingsOnly", "Drawings only.", "Solo dibujos.");
        Add(d, "Study.ReportFooter", "Made with Hoshi · {0}", "Hecho con Hoshi · {0}");

        Add(d, "Study.Quiz", "What would you play?", "¿Qué jugarías?");
        Add(d, "Study.QuizTip", "A quiz from your pins: the position before each pinned move, find the right one", "Un test con tus pins: la posición antes de cada jugada marcada, encuentra la buena");
        Add(d, "Study.QuizEmpty", "Pin some mistakes, good moves or key moments first.", "Primero marca algunos errores, buenas jugadas o momentos clave.");
        Add(d, "Study.QuizProgress", "Question {0} of {1}", "Pregunta {0} de {1}");
        Add(d, "Study.QuizPrompt", "{0} to play. Click your move.", "Juegan {0}. Haz clic en tu jugada.");
        Add(d, "Study.QuizScore", "{0} / {1}", "{0} / {1}");
        Add(d, "Study.QuizRight", "Right!", "¡Correcto!");
        Add(d, "Study.QuizWrong", "Not quite: {0} (green).", "No exactamente: {0} (verde).");
        Add(d, "Study.QuizCompare", "Compare with the note: did you find it?", "Compara con la nota: ¿la encontraste?");
        Add(d, "Study.QuizRevealed", "Here is what the note says.", "Esto dice la nota.");
        Add(d, "Study.QuizSelfRight", "Counted as right.", "Cuenta como acierto.");
        Add(d, "Study.QuizSelfWrong", "Counted as a miss.", "Cuenta como fallo.");
        Add(d, "Study.QuizDone", "Done: {0} of {1} right.", "Terminado: {0} de {1} acertadas.");
        Add(d, "Study.QuizReveal", "Show", "Mostrar");
        Add(d, "Study.QuizNext", "Next", "Siguiente");
        Add(d, "Study.QuizEnd", "End quiz", "Terminar");
        Add(d, "Study.QuizGotIt", "I had it", "La tenía");
        Add(d, "Study.QuizMissed", "I missed it", "No la vi");
        Add(d, "Study.QuizPlayed", "! = the move played", "! = la jugada de la partida");
    }
}

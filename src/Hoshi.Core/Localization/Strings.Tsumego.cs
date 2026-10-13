namespace Hoshi.Core.Localization;

public static partial class Tr
{
    /// <summary>Problems (tsumego).</summary>
    static partial void AddTsumego(Dictionary<string, (string En, string Es)> d)
    {
        Add(d, "Tsumego.Title", "Problems", "Problemas");
        Add(d, "Tsumego.Menu", "Problems (tsumego)…", "Problemas (tsumego)…");
        Add(d, "Tsumego.ToPlay", "{0} to play.", "Juegan {0}.");
        Add(d, "Tsumego.Blocked", "Problems are closed during your live OGS games.", "Los problemas se cierran durante tus partidas de OGS en curso.");
        Add(d, "Tsumego.Progress", "{0} of {1} solved", "{0} de {1} resueltos");
        Add(d, "Tsumego.Solved", "Right!", "¡Correcto!");
        Add(d, "Tsumego.SolvedWithHelp", "Solved with the answer: it comes back soon.", "Resuelto con la respuesta: volverá pronto.");
        Add(d, "Tsumego.Wrong", "Not this time. Retry or look at the answer.", "Esta vez no. Reintenta o mira la respuesta.");
        Add(d, "Tsumego.KeepGoing", "Good. Keep going.", "Bien. Sigue.");
        Add(d, "Tsumego.Illegal", "That move is not allowed.", "Esa jugada no está permitida.");
        Add(d, "Tsumego.AnswerShown", "The circle shows the next move.", "El círculo muestra la siguiente jugada.");
        Add(d, "Tsumego.NoProblems", "No problems in that file (they need a position and an answer).", "No hay problemas en ese archivo (necesitan una posición y una respuesta).");
        Add(d, "Tsumego.Imported", "{0} problems from {1}.", "{0} problemas de {1}.");
        Add(d, "Tsumego.New", "new", "nuevo");
        Add(d, "Tsumego.ToRedo", "to redo", "por repetir");
        Add(d, "Tsumego.Due", "to review", "por repasar");
        Add(d, "Tsumego.Box", "box {0}", "caja {0}");
        Add(d, "Tsumego.Retry", "Retry", "Reintentar");
        Add(d, "Tsumego.Next", "Next", "Siguiente");
        Add(d, "Tsumego.Previous", "Previous", "Anterior");
        Add(d, "Tsumego.Answer", "Answer", "Respuesta");
        Add(d, "Tsumego.Import", "Open a collection…", "Abrir colección…");
        Add(d, "Tsumego.ImportHint", "Any SGF problem collection works: each game is a problem, \"RIGHT\" in a comment marks the answer.", "Sirve cualquier colección de problemas en SGF: cada partida es un problema y «RIGHT» en un comentario marca la respuesta.");

        Add(d, "Tsumego.Ask.Capture", "Black to play: capture the white stones.", "Juegan negras: captura las piedras blancas.");
        Add(d, "Tsumego.Ask.Kill", "Black to play: kill the white group.", "Juegan negras: mata al grupo blanco.");
        Add(d, "Tsumego.Ask.Live", "Black to play: live.", "Juegan negras: vive.");
        Add(d, "Tsumego.P1", "Atari", "Atari");
        Add(d, "Tsumego.P2", "In the corner", "En la esquina");
        Add(d, "Tsumego.P3", "Snapback", "Recaptura (snapback)");
        Add(d, "Tsumego.P4", "Double atari", "Doble atari");
        Add(d, "Tsumego.P5", "Straight three: kill", "Tres en línea: matar");
        Add(d, "Tsumego.P6", "Straight three: live", "Tres en línea: vivir");
        Add(d, "Tsumego.P7", "Bent three: kill", "Tres doblado: matar");
        Add(d, "Tsumego.P8", "Bent three: live", "Tres doblado: vivir");
    }
}

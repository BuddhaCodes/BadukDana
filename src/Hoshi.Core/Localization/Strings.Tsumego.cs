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

        // The first-start tour.
        Add(d, "Welcome.Menu", "Welcome tour", "Recorrido de bienvenida");
        Add(d, "Welcome.Next", "Next", "Siguiente");
        Add(d, "Welcome.Back", "Back", "Atrás");
        Add(d, "Welcome.Skip", "Skip", "Saltar");
        Add(d, "Welcome.Done", "Start playing", "A jugar");
        Add(d, "Welcome.Rules", "Learn the rules", "Aprender las reglas");
        Add(d, "Welcome.Play.Title", "Welcome to Hoshi 星", "Bienvenido a Hoshi 星");
        Add(d, "Welcome.Play.Text", "Click a point to play. Two players can share this board, or play KataGo from the menu (Ctrl+G). The arrow keys and the mouse wheel step through the game; P passes.", "Haz clic en un punto para jugar. Dos personas pueden compartir este tablero, o jugar contra KataGo desde el menú (Ctrl+G). Las flechas y la rueda del ratón recorren la partida; P pasa.");
        Add(d, "Welcome.Play.Phone", "Tap a point to see the stone, tap it again to play. Two players can share the phone, or play KataGo from the menu. Slide sideways on the names to step through the game.", "Toca un punto para ver la piedra y tócalo otra vez para jugar. Dos personas pueden compartir el teléfono, o jugar contra KataGo desde el menú. Desliza a los lados sobre los nombres para recorrer la partida.");
        Add(d, "Welcome.Analysis.Title", "KataGo, from the first move", "KataGo, desde la primera jugada");
        Add(d, "Welcome.Analysis.Text", "KataGo rates every move in the background: the good ones make the board shake, mistakes turn red in the tree. Analysis (A) shows its suggestions and the score graph; T shows the territory.", "KataGo valora cada jugada en segundo plano: las buenas hacen temblar el tablero y los errores se ven en rojo en el árbol. Análisis (A) muestra sus sugerencias y la gráfica; T muestra el territorio.");
        Add(d, "Welcome.Study.Title", "Study and review", "Estudiar y repasar");
        Add(d, "Welcome.Study.Text", "Study (S) pins notes on the moments that matter, draws arrows and \"what if\" lines, and tests you with \"What would you play?\". Share the study as an SGF: your friends' notes merge into yours.", "Estudio (S) fija notas en los momentos clave, dibuja flechas y líneas «¿y si…?» y te pone a prueba con «¿Qué jugarías?». Comparte el estudio en SGF: las notas de tus amigos se suman a las tuyas.");
        Add(d, "Welcome.Learn.Title", "Problems and joseki", "Problemas y josekis");
        Add(d, "Welcome.Learn.Text", "Problems (Ctrl+P) train life and death; the joseki trainer (Ctrl+J) drills corner sequences. Both bring back what you got wrong, until you know it.", "Problemas (Ctrl+P) entrena vida y muerte; el entrenador de josekis (Ctrl+J) repite secuencias de esquina. Ambos vuelven a lo que fallaste hasta que lo sabes.");
        Add(d, "Welcome.Online.Title", "Play online", "Jugar en línea");
        Add(d, "Welcome.Online.Text", "Sign in to online-go.com from Online (Ctrl+L): challenges, automatch and your correspondence games, with a notice when it is your move. New to Go? Start with the rules: four small lessons.", "Entra en online-go.com desde En línea (Ctrl+L): desafíos, automatch y tus partidas por correspondencia, con aviso cuando te toca. ¿Nuevo en el Go? Empieza por las reglas: cuatro lecciones breves.");

        // The rules, as four small lessons.
        Add(d, "Rules.Source", "Rules", "Reglas");
        Add(d, "Rules.L1", "1 · Liberties", "1 · Libertades");
        Add(d, "Rules.L1.Ask", "Stones live by their liberties: the empty points next to them. This white stone has one left. Black to play: take it away.", "Las piedras viven de sus libertades: los puntos vacíos a su lado. A esta piedra blanca le queda una. Juegan negras: quítasela.");
        Add(d, "Rules.L1.Right", "A stone without liberties is captured and leaves the board.", "Una piedra sin libertades queda capturada y sale del tablero.");
        Add(d, "Rules.L2", "2 · Escape", "2 · Escapar");
        Add(d, "Rules.L2.Ask", "Your stone has one liberty left (atari). Black to play: run away to gain liberties.", "A tu piedra le queda una libertad (atari). Juegan negras: escapa para ganar libertades.");
        Add(d, "Rules.L2.Right", "Two stones side by side share their liberties: now there are three.", "Dos piedras juntas comparten sus libertades: ahora son tres.");
        Add(d, "Rules.L3", "3 · Groups", "3 · Grupos");
        Add(d, "Rules.L3.Ask", "Connected stones are captured together. Black to play: capture both white stones on the edge.", "Las piedras conectadas se capturan juntas. Juegan negras: captura las dos piedras blancas del borde.");
        Add(d, "Rules.L3.Right", "The edge is not a liberty: groups there are easier to capture.", "El borde no es una libertad: los grupos ahí se capturan más fácil.");
        Add(d, "Rules.L4", "4 · Two eyes", "4 · Dos ojos");
        Add(d, "Rules.L4.Ask", "A group lives forever with two separate eyes (inner points the opponent cannot fill). Black to play: make two eyes.", "Un grupo vive para siempre con dos ojos separados (puntos interiores que el rival no puede llenar). Juegan negras: haz dos ojos.");
        Add(d, "Rules.L4.Right", "Two eyes: White can never fill both. The game ends when both players pass; the one with more territory and captures wins.", "Dos ojos: blanco nunca podrá llenar ambos. La partida termina cuando los dos pasan; gana quien tenga más territorio y capturas.");

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

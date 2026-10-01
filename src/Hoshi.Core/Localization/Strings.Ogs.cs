namespace Hoshi.Core.Localization;

public static partial class Tr
{
    static partial void AddOgs(Dictionary<string, (string En, string Es)> d)
    {
        // Auth
        Add(d, "Ogs.PasswordServer", "This server uses username and password sign-in.", "Este servidor usa inicio de sesión con usuario y contraseña.");
        Add(d, "Ogs.ClientIdMissing", "Ogs:ClientId is missing from the configuration (id of the registered OAuth application).", "Falta Ogs:ClientId en la configuración (id de la aplicación OAuth registrada).");
        Add(d, "Ogs.NoUserAfterOAuth", "OGS accepted the sign-in but did not return the user's data.", "OGS aceptó el inicio de sesión pero no devolvió los datos del usuario.");
        Add(d, "Ogs.PasswordDevOnly", "Password sign-in is only available in development mode (beta).", "El inicio de sesión con contraseña solo está disponible en el modo de desarrollo (beta).");
        Add(d, "Ogs.WrongCredentials", "Wrong username or password.", "Usuario o contraseña incorrectos.");
        Add(d, "Ogs.LoginHttpError", "OGS answered {0} to the sign-in.", "OGS respondió {0} al iniciar sesión.");
        Add(d, "Ogs.NoUserAfterLogin", "OGS did not return the user's data after signing in (two-step verification or SSO?).", "OGS no devolvió los datos del usuario tras iniciar sesión (¿verificación en dos pasos o SSO?).");
        Add(d, "Ogs.NoRefreshToken", "There is no refresh token.", "No hay token de actualización.");
        Add(d, "Ogs.TokenRejected", "OGS rejected the token request ({0}).", "OGS rechazó la solicitud de token ({0}).");
        Add(d, "Ogs.ConfigHttpError", "OGS answered {0} when asked for ui/config.", "OGS respondió {0} al pedir ui/config.");

        // OAuth loopback page and errors
        Add(d, "Ogs.PortBusy", "Port {0} on 127.0.0.1 is in use; OGS's answer cannot be received.", "El puerto {0} de 127.0.0.1 está ocupado; no se puede recibir la respuesta de OGS.");
        Add(d, "Ogs.Page.NotSignedInTitle", "Not signed in", "No se inició sesión");
        Add(d, "Ogs.Page.CancelledBody", "OGS cancelled the authorization. You can close this tab.", "OGS canceló la autorización. Puedes cerrar esta pestaña.");
        Add(d, "Ogs.AuthorizationError", "OGS returned an authorization error: {0}", "OGS devolvió un error de autorización: {0}");
        Add(d, "Ogs.Page.InvalidTitle", "Invalid request", "Solicitud no válida");
        Add(d, "Ogs.Page.StateMismatchBody", "The answer does not belong to this sign-in.", "La respuesta no corresponde a este inicio de sesión.");
        Add(d, "Ogs.StateMismatch", "The state parameter does not match; the answer was discarded.", "El parámetro state no coincide; se descartó la respuesta.");
        Add(d, "Ogs.Page.CodeMissingBody", "The authorization code is missing.", "Falta el código de autorización.");
        Add(d, "Ogs.CodeMissing", "The redirect does not include an authorization code.", "La redirección no incluye un código de autorización.");
        Add(d, "Ogs.Page.SignedInTitle", "Signed in", "Sesión iniciada");
        Add(d, "Ogs.Page.SignedInBody", "Hoshi now has access to your OGS account. You can close this tab.", "Hoshi ya tiene acceso a tu cuenta de OGS. Puedes cerrar esta pestaña.");

        // REST / realtime
        Add(d, "Ogs.HttpStatus", "OGS answered {0} {1}.", "OGS respondió {0} {1}.");
        Add(d, "Ogs.NotConnected", "No connection to the OGS server.", "Sin conexión con el servidor de OGS.");
        Add(d, "Ogs.ConnectionLost", "The connection to OGS was lost before an answer arrived.", "Se perdió la conexión con OGS antes de recibir respuesta.");

        // Time controls
        Add(d, "Ogs.Time.Byoyomi", "byoyomi {0} + {1}×{2}", "byoyomi {0} + {1}×{2}");
        Add(d, "Ogs.Time.Fischer", "fischer {0} + {1} (max. {2})", "fischer {0} + {1} (máx. {2})");
        Add(d, "Ogs.Time.Canadian", "canadian {0} + {1}/{2}", "canadiense {0} + {1}/{2}");
        Add(d, "Ogs.Time.Simple", "simple {0}/move", "simple {0}/jugada");
        Add(d, "Ogs.Time.Absolute", "absolute {0}", "absoluto {0}");
        Add(d, "Ogs.Time.None", "no limit", "sin límite");

        // Results
        Add(d, "Ogs.Result.Annulled", "Game annulled", "Partida anulada");
        Add(d, "Ogs.Result.BlackWins", "Black wins", "Ganan negras");
        Add(d, "Ogs.Result.WhiteWins", "White wins", "Ganan blancas");
        Add(d, "Ogs.Result.ByResignation", "{0} by resignation", "{0} por abandono");
        Add(d, "Ogs.Result.ByTime", "{0} on time", "{0} por tiempo");
        Add(d, "Ogs.Result.ByPoints", "{0} by {1} points", "{0} por {1} puntos");
    }
}

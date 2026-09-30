# Arquitectura

## Capas

```
┌──────────────────────── Hoshi.App (Avalonia) ────────────────────────┐
│ Views (.axaml)  ←binding→  ViewModels  ←→  Services (DI)             │
│ GoBoardControl (render)    MainVM, BoardVM, GameTreeVM, OgsLobbyVM…  │
│ OgsGameCoordinator: aplica eventos de Ogs al GameCursor (Sgf)        │
└──────────────┬────────────────────────┬──────────────────────────────┘
               │                        │
        Hoshi.Sgf                 Hoshi.Ogs
   (árbol, parse/serialize,   (auth, REST, WebSocket,
    GameCursor)                eventos de dominio)
               │                        │
               └────────── Hoshi.Core ──┘
             (reglas puras, inmutables, sin I/O)
```

Regla de dependencias (la comprueba `tests/Hoshi.App.Tests/ArchitectureTests.cs`):
`App → Ogs, Sgf, Core` · `Ogs → Core` · `Sgf → Core` · `Engines → Core` · `Core → (nada)`.

## Hoshi.Core — modelos (implementados en la Fase 1)

- `Stone` — `enum { Empty, Black, White }` + `Opponent()`.
- `Point` — `readonly record struct (int X, int Y)`, origen arriba-izquierda.
  - `ToSgf()` / `FromSgf()` / `TryParseSgf()`; `IsSgfPass(value, size)` (`""` o `"tt"` en ≤19×19).
  - `ToHuman(height)` / `FromHuman()` / `TryParseHuman()` (`"D16"`, sin la I, hasta 25 columnas).
- `RuleSet` — record con `Ko` (`Simple`, `PositionalSuperko`, `SituationalSuperko`), `AllowSuicide`, `Scoring` (`Territory`/`Area`), `DefaultKomi`, `HandicapCompensation`.
  - Preajustes con los nombres de OGS: `japanese`, `korean`, `chinese`, `aga`, `nz`, `ing` (`RuleSet.FromName`). Ing se aproxima con superko posicional.
- `BoardState` — posición inmutable, 2–25 líneas, también rectangular.
  - `TryPlay(color, point) → MoveResult` (`State`, `Captured`, `Reason`: `OutOfBounds`, `Occupied`, `Suicide`, `Ko`, `Superko`); `Play()` lanza `IllegalMoveException`.
  - `Pass()`, `Setup()` (AB/AW/AE sin capturas), `WithToMove()`, `WithHandicap()` (libre), `WithFixedHandicap()`.
  - `Hash` Zobrist determinista (semilla fija) de las piedras; historial inmutable de posiciones para superko (situacional incluye el turno).
  - `BlackCaptures`/`WhiteCaptures`, `KoPoint`, `ToMove`, `GetGroup()`, `GetLiberties()`.
  - Se aceptan jugadas de cualquier color (como permite SGF); `ToMove` pasa al contrario.
- `Handicap` — `FixedPoints(size, n)`, `MaxFixed(size)` (9 en impares ≥7, 4 en pares, 0 bajo 7×7).
- `Scoring.Score(board, deadStones, komi, handicap) → ScoreResult` — territorio o área según el `RuleSet`; las piedras muertas cuentan como prisioneros y su punto como territorio; las regiones que tocan ambos colores son dame (seki). Los ojos en seki cuentan como territorio (modo no estricto, como OGS). `OwnerAt(point)` para el overlay de territorio.
- `GameClock` — modelos de tiempo (absoluto, Fischer, byo-yomi, canadiense, simple) — **pendiente, Fase 5**.

## Hoshi.Sgf (implementado en la Fase 3)

- `GameTree` (raíz + `Info`) / `GameNode`: propiedades ordenadas con valores sin escapar, hijos ordenados (el primero es la línea principal), `Parent`, `Descendants()`.
  - Ayudas tipadas: `GetMove(size) → SgfMove?` (pase = `""` o `tt`), `GetPoints(id)` (expande listas comprimidas `aa:cc`), `GetMarkup() → Markup(Point, MarkupKind, Text?)`, `Comment`.
- `GameInfo`: vista tipada del nodo raíz (SZ con tamaño rectangular, KM, HA, RU con alias de otros programas, PB/BR/PW/WR, DT, EV, RE, GN, PC). Asignar vacío elimina la propiedad.
- `SgfParser`: tolerante (texto previo, `)` final ausente, valores sin cerrar, identificadores FF[3] en minúsculas, varios juegos por archivo) con `Warnings`. Decodifica bytes por BOM → `CA` → UTF-8 estricto → ISO-8859-1; soporta páginas de código asiáticas.
- `SgfWriter`: FF[4] en UTF-8, siempre con `FF[4]GM[1]CA[UTF-8]AP[Hoshi:versión]`, orden de propiedades fijo (raíz/info, jugadas y colocación, anotaciones, marcas, resto alfabético) y escritura idempotente.
- `GameCursor`: nodo actual, `BoardState` por nodo calculado de forma perezosa e iterativa con caché (invalidada al editar), recuerda la última variante visitada.
  - Navegación: `Next/Previous/First/Last/NextVariation/PreviousVariation/GoTo`.
  - Edición: `Play(Point?)` (reutiliza el hijo si la jugada ya existe; si no, crea variante), `ToggleSetupStone` (AB/AW/AE; en un nodo con jugada crea un hijo), `ToggleMarkup`, `NextFreeLabel`, `Comment`, `DeleteCurrent`, `PromoteToMainLine`.
  - Jugadas ilegales en un archivo se colocan igualmente y se informan en `Warnings`. Tableros > 25 → `NotSupportedException`.

## Hoshi.Ogs

- `OgsOptions` — servidor, modo de autenticación (`OAuth` | `Password`), `ClientId`, puerto loopback.
- `OgsAuthService` (`IOgsCredentials`) — OAuth2 + PKCE con `LoopbackRedirectListener`, refresh, revocación; login web solo en beta; `Session` (usuario + JWT). Guarda el refresh token en un `ITokenStore`.
- `OgsRestClient` — partidas activas (`ui/overview`), desafíos, búsqueda de jugadores.
- `OgsRealtimeClient` — WebSocket (`IWebSocketConnection`), `authenticate`, ping/latencia/deriva, reconexión con back-off, cola de salida, `RequestAsync`, `Subscribe`.
- `OgsSeekGraph` (desafíos abiertos en vivo) y `ChallengeKeepAlive` (espera de rival).
- `OgsGameSession` — una partida en curso **sin conocer el árbol SGF**:
  - Entrada: `gamedata` inicial → `OgsGameSnapshot` (reglas, tamaño, handicap, komi, jugadores, lista de jugadas como `Point?`, reloj, fase).
  - Eventos: `MoveReceived(moveNumber, color, Point?)`, `MoveRejected`, `ClockUpdated`, `PhaseChanged`, `RemovedStonesChanged`, `UndoRequested/Accepted/Canceled`, `ChatReceived`, `GameEnded(score, winner, outcome)`.
  - Comandos: `SendMoveAsync(Point?)`, `ResignAsync`, `RequestUndoAsync`, `SetRemovedStonesAsync`, `AcceptRemovedStonesAsync`, `SendChatAsync`.
  - Solo usa tipos de Core (`Point`, `Stone`, `RuleSet`); valida localmente con `BoardState` para detectar desincronizaciones.
- Los DTO de OGS **no salen** de este proyecto: se mapean a modelos de Core.

## Hoshi.Core — territorio
- `TerritoryEstimator.Estimate(board, komi)`: regiones vacías cerradas por un solo color (≤ 1/3 del tablero) = seguro (±1); el resto recibe la influencia de las piedras (exponencial, alcance 6), acotada a ±0.75 para no confundirse con lo seguro. No lee: no detecta piedras muertas.
- `TerritoryEstimate`: propiedad por punto en [-1, 1] (+ = negras), seguro (|o| ≥ 0.8), potencial (valor esperado), piedras muertas si el mapa las da al rival, y `Lead` (territorio + capturas − komi). `FromOwnership` lo construye desde el mapa de KataGo.

## Hoshi.Engines
- `KataGoAnalysisEngine`: motor de análisis JSON de KataGo (una consulta por línea, una respuesta por turno, por `id`; `terminate` al cancelar; reinicia el proceso si muere). `KataGoProcess` lanza `katago analysis … -override-config reportAnalysisWinratesAs=BLACK`: todos los valores llegan desde el punto de vista de negras.
- `MoveReview.Assess(antes, después, jugada)`: puntos y winrate perdidos respecto a la mejor candidata, desde el punto de vista de quien juega.

## Hoshi.App

### Ventanas y vistas
- `MainWindow`: tablero central + barra lateral derecha (plegable) + barra inferior de modo.
- Sidebar: `PlayerInfoPanel` (nombres, rango, capturas, relojes), `GameTreePanel` (grafo de variantes), `CommentPanel`, `ChatPanel` (en modo OGS).
- `LobbyWindow` (Ctrl+L, botón «En línea»): pestañas "Mis partidas", "Desafíos abiertos", "Automatch", "Crear desafío".
- Diálogos: Nueva partida local, Info de partida (propiedades SGF), Preferencias, Login OGS.

### OgsGameCoordinator (decisión 2026-09-29; implementado como `OnlineGameViewModel`, Fase 5)
- `IOnlineGame` (App) envuelve `OgsGameSession` para poder probar con un falso. `MainWindowViewModel.OpenOnlineGameAsync` crea el `OnlineGameViewModel` y lo conecta; `GameViewModel.LoadOnline` enruta clics, pasar, marcadores (piedras muertas) y el estado al coordinador.
- El árbol es la línea principal: `gamedata` lo reconstruye (AB/AW/PL/HA/KM/PB/PW…), cada jugada se añade al final y la vista la sigue solo si el usuario estaba en la última; deshacer aceptado recorta. El turno sale de las jugadas recibidas (`ColorForMove`), no del reloj.
- Vive en App y es el único que conoce a la vez `OgsGameSession` y `GameCursor`.
- Al recibir `gamedata` construye un `GameTree` con las jugadas; en cada `MoveReceived` añade el nodo (o confirma la jugada pendiente) y mueve el cursor si el usuario estaba en la última jugada.
- Alternativa descartada: permitir `Ogs → Sgf`. Se descartó para mantener Ogs testeable con fixtures sin el modelo SGF y para no acoplar el protocolo al árbol de variantes.

### GoBoardControl
- Control personalizado que hereda de `Control` y sobreescribe `Render(DrawingContext)`.
- Capas de dibujo: fondo de madera → líneas y hoshi → coordenadas → piedras (con sombra) → marcadores (último movimiento, etiquetas, formas) → piedra fantasma al pasar el ratón → overlays (territorio, heatmap futuro).
- Escala al tamaño disponible manteniendo proporción cuadrada; usa `RenderTargetBitmap`/cachés para texturas.
- Expone: `BoardState`, `Markers`, `ShowCoordinates`, `LastMove`, `IsInteractive`, evento `PointClicked(Point, MouseButton)`.
- Animación sutil al colocar piedra (opcional, desactivable).

### GameViewModel y paneles (Fase 3)
- `GameViewModel` envuelve un `GameCursor`: jugar/pasar/deshacer (deshacer borra la jugada si es hoja), navegación, modo edición con `EditTool`, abrir/guardar (`IFileDialogService`), info de partida y confirmaciones (`IDialogService`), título con `*` si hay cambios.
- `GameTreeLayout` (filas = profundidad; línea principal en la columna 0; cada variante en la primera columna libre para todo su subárbol) y `GameTreeControl` (dibujo, clic para navegar, auto-scroll al nodo actual).
- Barra lateral: jugadores/capturas, árbol, comentario. Barra inferior con navegación, menú Archivo y, en modo edición, herramientas.
- Atajos de una letra (P) solo en el área del tablero, para que no se disparen al escribir un comentario.

### Análisis (Fase 7)
- `AnalysisViewModel` (en `MainWindowViewModel.Analysis`): en cada cambio de posición pide a KataGo los turnos `n−1` y `n` que falten en su caché (clave = setup + jugadas), valora la jugada, muestra sugerencias y rellena la gráfica de la línea en tandas de 25 turnos a ¼ de las visitas. Se bloquea con `Online.IsPlayer && !IsFinished`.
- `IAnalysisEngine` / `AnalysisEngineHost`: KataGo según Preferencias; `GoBoardControl.Territory` y `.Suggestions` dibujan las capas; `ScoreGraph` la gráfica.

### Servicios de App
- `IDialogService`, `IFileDialogService`; futuros `ISettingsService` (JSON en carpeta de datos), `ISoundService`.
- `SecureTokenStore` → `ITokenStore` del SO con respaldo en memoria (implementa el `ISecureStore` de CLAUDE.md).
- `IOgsClient` / `OgsClient` — fachada que une auth, REST, tiempo real y seek graph para el lobby; `LobbyViewModel` solo depende de ella (tests con un falso). `IUiDispatcher` lleva los eventos al hilo de UI.
- `OgsServerCatalog` — los dos servidores fijos: online-go.com (siempre OAuth, `Ogs:ClientId`) y beta (siempre contraseña); `Ogs:DefaultServer` elige el inicial. `OgsClient` crea bajo demanda una `OgsConnection` (auth + REST + tiempo real) por servidor mediante `IOgsConnectionFactory` y cambia de servidor solo sin sesión.
- `OgsServiceRegistration.AddOgs` — `HttpClient` con nombre `ogs` (`IHttpClientFactory`, `UseCookies=false`; `BaseAddress` por conexión).
- `ILobbyWindowService` — ventana "Jugar en línea" (no modal, única).
- `AppPaths.DataDirectory`: `%LOCALAPPDATA%\Hoshi`, `~/Library/Application Support/Hoshi`, `~/.local/share/Hoshi`. Logs en `logs/hoshi-YYYYMMDD.log` (14 días).

## Flujo: jugada en OGS

1. Usuario hace clic → `GoBoardControl.PointClicked` → `BoardVM.PlayCommand`.
2. `BoardVM` valida localmente con `BoardState.TryPlay` (feedback instantáneo, piedra "pendiente").
3. `OgsGameCoordinator` llama a `OgsGameSession.SendMoveAsync(point)` → `game/move` por WebSocket.
4. El servidor emite `game/{id}/move` → la sesión emite `MoveReceived` → el coordinador confirma el nodo pendiente (o lo revierte si llega `game/{id}/error` / `MoveRejected`).
5. Reloj actualizado vía `game/{id}/clock` → `ClockUpdated`; la UI interpola localmente entre mensajes.

## Hilos
- Todo evento de red se traslada al hilo de UI con `Dispatcher.UIThread.Post` (en el coordinador, no en Ogs).
- `Hoshi.Core` es thread-safe por ser inmutable.

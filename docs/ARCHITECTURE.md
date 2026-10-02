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
- `AnalysisViewModel` (en `MainWindowViewModel.Analysis`): KataGo trabaja **siempre** que esté configurado y permitido (`IsEngineActive`, desde el arranque; `IsWarmingUp` hasta la primera respuesta); `IsAnalysisOn` solo decide qué se muestra (`BoardSuggestions`, panel, gráfica). En cada cambio de posición pide a KataGo, **en vivo**, los turnos `n−1` y `n` que no estén completos (clave = setup + jugadas): prioridad 10 y `reportDuringSearchEvery` 0,25 s, así que el panel y las sugerencias se actualizan con resultados parciales mientras KataGo profundiza hasta las visitas máximas. Los parciales quedan en caché aunque la búsqueda se cancele al jugar; por eso la valoración de una jugada suele ser instantánea (la jugada ya estaba entre los candidatos de `n−1`). La celebración exige ≥ 40 visitas. La gráfica se rellena en tandas de 25 turnos a ¼ de las visitas con prioridad −10. Se bloquea con `Online.IsPlayer && !IsFinished`.
- `IAnalysisEngine` / `AnalysisEngineHost`: KataGo según Preferencias; `GoBoardControl.Territory` y `.Suggestions` dibujan las capas; `ScoreGraph` la gráfica. `Activity` informa de la carga/calibración de KataGo (leída de su stderr).
- Celebración: `GameViewModel.MovePlayed` marca la jugada; cuando llega su valoración, `AnalysisViewModel` publica `Impact` (`BoardImpact`: punto, fuerza 1–3, id) que `GoBoardControl` anima con `ImpactEffect`, y pide el sonido a `ISoundService`.

### Entrenador de josekis

- `Hoshi.Sgf.Joseki` (puro, con tests): `Symmetry` (8 simetrías), `JosekiLine` (id canónico = la menor de sus 8 grafías), `JosekiLibraryReader.ExtractLines` (una línea por hoja, nombre del último `N[]`, sin pases), `JosekiDrill` (una práctica: `Attempt` → Correct/Completed/AlsoJoseki/Wrong/Ignored, sigue la imagen en espejo, `Hint` tras dos fallos) y `JosekiCard`/`Leitner` (cajas y elección de la siguiente línea con reloj explícito).
- `Hoshi.App.Services.Joseki.JosekiLibrary`: recurso incrustado `Joseki/starter.sgf` (textos como claves `Joseki.Starter.*` traducidas por `Tr`) + `joseki/*.sgf` del usuario (importados y `my-lines.sgf`, que fusiona prefijos comunes) + `joseki/progress.json`.
- `JosekiTrainerViewModel` se muestra en la ventana principal: `MainWindow` tiene un segundo `GoBoardControl` (`JosekiBoard`) y un panel sobre la barra lateral (`JosekiPanel`) visibles con `Joseki.IsActive`; la partida no se toca. Filtro `JosekiFamilies` + lista `Rows`; `FetchFromOgs` recorre el explorador de OGS. `IsBlocked` igual que el análisis (partida OGS en curso del jugador) y cierra el entrenador.
- `Hoshi.Core.Corners`: secuencia local de cada esquina (10×10 líneas sin la punta central; tenuki insertado cuando un color juega dos veces seguidas) y las dos simetrías entre esquinas. `Hoshi.Core.Symmetry` (8 simetrías) vive en Core.
- `Hoshi.Ogs.Joseki`: `OgsJosekiClient` (OJE, ver `OGS_API.md`), `JosekiExplorer` (sigue una secuencia desde la raíz, deduce la esquina del explorador).
- `JosekiAssistantViewModel` («josekis naturales», `AppSettings.JosekiHints`): en cada `TreeVersion`, por esquina: `JosekiMatcher` (biblioteca local, colores relativos) + explorador (ambas simetrías) → `BoardJosekiHint` (punto, categoría, local) que `GoBoardControl.JosekiHints` dibuja con los colores del explorador; `Text`/`Description` de la esquina de la última jugada.

### Pelea y biblioteca de partidas
- `Hoshi.Core.FightMeter`: lee cada jugada (contacto con piedras rivales, grupos con 1–3 libertades a ≤2 casillas, capturas) → `FightReading(Intensity, Heat, Strength)`; el calor sube mientras las jugadas siguen en la misma zona (radio 4) y se enfría con tenuki. `AnalysisViewModel` lo usa cuando KataGo no está activo (OGS en curso o sin configurar): impacto y sonido según `Strength`, y `BattleHeat` → `MusicDirector.OnBattleHeat`.
- `GameViewModel.MoveSettled` (tableros antes/después, punto, nodo) y `TreeReplacing` (el árbol que se va). `ReplayRecorder` guarda en `IReplayStore` la partida que se va (local: id propio; OGS: `ogs-<id>`, también al terminar; réplica: solo si cambió). `MainWindowViewModel.OpenReplay` carga al principio con `Game.IsReview = true`: cada paso adelante espera la valoración de KataGo y celebra como una jugada.

### KataGo incluido

- `Services/KataGo/KataGoSetup.cs`: `KataGoLocator.Resolve` elige, por orden, las rutas de Preferencias, `<app>/katago/` (descargas de Windows/Linux), `<datos>/katago/` («Instalar KataGo») o un katago del sistema con la red de Hoshi; usa `hoshi_analysis.cfg` (escrito en `<datos>/katago/`) y `-override-config logDir=<datos>/katago/logs`. `KataGoInstaller` descarga el zip oficial (URL, tamaño y SHA-256 fijados) y el recurso `Hoshi-katago-b10c128.txt.gz` de la última versión de Hoshi.
- `AnalysisEngineHost.Source` dice de dónde viene; `KataGoSetupViewModel` muestra el aviso «Instalar KataGo» en el tablero y en Preferencias.
- En desarrollo, una copia local en `src/Hoshi.App/Assets/katago/` (ignorada por git) se copia a `bin/…/katago/` y cuenta como incluida.

### Actualizaciones

- `Services/Updates`: `GitHubReleaseSource` (API pública `releases/latest`; `HOSHI_UPDATE_FEED` la sustituye en pruebas), `UpdatePlatform` (versión propia, archivo por SO/arquitectura, tipo de instalación: desarrollo, carpeta o `Hoshi.app`), `UpdateInstaller` (extrae, renombra lo reemplazado a `*.old`, mueve lo nuevo; en macOS cambia el bundle entero con `ditto`; limpia al arrancar) y `UpdateService` (descarga con progreso, verifica `SHA256SUMS.txt`, lanza la nueva versión con `--wait-for-pid`).
- `UpdateViewModel`: comprueba a los 8 s y cada 12 h (`AppSettings.CheckForUpdates`, `SkippedUpdate`), aviso en la esquina del tablero (Actualizar y reiniciar · Más tarde · Saltar), menú «Buscar actualizaciones…». Una copia que no puede reemplazarse (desde el código o en una carpeta sin permiso) abre la página de la versión. No instala durante una partida de OGS en curso.
- `Program.Main` llama primero a `UpdateService.WaitForPreviousInstance`, para que la versión vieja termine de cerrarse.

### Localización
- `Hoshi.Core.Localization.Tr`: tabla clave → (inglés, español), sin dependencias; cada capa añade sus textos en `Strings.<Capa>.cs` (Core, Engines, Ogs, App, Views). `Tr.T(clave)` / `Tr.F(clave, args)` (formato invariante). Idioma por defecto inglés (`AppSettings.Language`); `Tr.LanguageChanged` refresca la UI.
- XAML: `{l:T Clave}` (`TExtension` → binding a `LocalizedStrings.Instance[clave]`), se actualiza en vivo. Los ViewModels refrescan todas sus propiedades al cambiar de idioma (`ViewModelBase`).
- Tests: corren en español (inicializador de módulo); `LocalizationTests` comprueba que toda clave usada en las vistas existe, que ambos idiomas tienen texto y los mismos marcadores `{n}`, y el cambio en vivo.

### Servicios de App
- `IDialogService`, `IFileDialogService`, `ISettingsService` (JSON en carpeta de datos).
- Música (`Services/Music`): `LofiComposer` sintetiza al arrancar un bucle de 8 compases (80 BPM, 24 s) en 5 capas sincronizadas; `MusicDirector` (lógica pura con reloj explícito) convierte los veredictos (`AnalysisViewModel.MoveJudged`) en «calor» 0–4; `MusicMixer` mezcla las capas con ganancias suaves, filtro paso bajo según el calor y efecto tape-stop; `MusicService` lo envía en un hilo propio a un `IPcmSink` del SO (waveOut de winmm en Windows; `pacat`/`aplay` en Linux; sin salida en macOS por ahora). Sin veredictos (partidas de OGS en curso) la música se queda tranquila: no revela nada del motor.
- `ISoundService` / `SystemSoundService`: sin librerías de audio; usa lo que trae el SO (MCI de winmm en Windows, `afplay` en macOS, `paplay`/`aplay` en Linux). Los WAV integrados se copian a `cache/sounds/`; un archivo con el mismo nombre (`.wav`/`.mp3`) en `sounds/` de la carpeta de datos los sustituye.
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

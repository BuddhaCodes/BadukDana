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

## Hoshi.Sgf

- `GameTree` / `GameNode`: nodo con propiedades (`B`, `W`, `AB`, `AW`, `C`, `LB`, `TR`, `SQ`, `CR`, `MA`...), hijos ordenados, padre.
- Parser tolerante (recupera errores, soporta escapes `\]`, múltiples juegos en un archivo, codificaciones vía `CA`).
- Serializer que produce SGF FF[4] válido y estable (orden de propiedades determinista para diffs limpios).
- `GameCursor`: navegación (siguiente, anterior, variante siguiente, ir a nodo) y cálculo perezoso de `BoardState` por nodo (con caché).

## Hoshi.Ogs

- `IOgsAuthService` — login OAuth2, refresh, logout, estado de sesión.
- `IOgsRestClient` — usuario actual, partidas activas, desafíos, SGF de partidas, perfiles.
- `IOgsRealtimeClient` — conexión WebSocket, autenticación con JWT, ping/latencia, reconexión, suscripción a partidas.
- `OgsGameSession` — una partida en curso **sin conocer el árbol SGF**:
  - Entrada: `gamedata` inicial → `OgsGameSnapshot` (reglas, tamaño, handicap, komi, jugadores, lista de jugadas como `Point?`, reloj, fase).
  - Eventos: `MoveReceived(moveNumber, color, Point?)`, `MoveRejected`, `ClockUpdated`, `PhaseChanged`, `RemovedStonesChanged`, `UndoRequested/Accepted/Canceled`, `ChatReceived`, `GameEnded(score, winner, outcome)`.
  - Comandos: `SendMoveAsync(Point?)`, `ResignAsync`, `RequestUndoAsync`, `SetRemovedStonesAsync`, `AcceptRemovedStonesAsync`, `SendChatAsync`.
  - Solo usa tipos de Core (`Point`, `Stone`, `RuleSet`); valida localmente con `BoardState` para detectar desincronizaciones.
- Los DTO de OGS **no salen** de este proyecto: se mapean a modelos de Core.

## Hoshi.App

### Ventanas y vistas
- `MainWindow`: tablero central + barra lateral derecha (plegable) + barra inferior de modo.
- Sidebar: `PlayerInfoPanel` (nombres, rango, capturas, relojes), `GameTreePanel` (grafo de variantes), `CommentPanel`, `ChatPanel` (en modo OGS).
- `OgsLobbyView`: pestañas "Mis partidas", "Desafíos abiertos", "Automatch", "Crear desafío".
- Diálogos: Nueva partida local, Info de partida (propiedades SGF), Preferencias, Login OGS.

### OgsGameCoordinator (decisión 2026-09-29)
- Vive en App y es el único que conoce a la vez `OgsGameSession` y `GameCursor`.
- Al recibir `gamedata` construye un `GameTree` con las jugadas; en cada `MoveReceived` añade el nodo (o confirma la jugada pendiente) y mueve el cursor si el usuario estaba en la última jugada.
- Alternativa descartada: permitir `Ogs → Sgf`. Se descartó para mantener Ogs testeable con fixtures sin el modelo SGF y para no acoplar el protocolo al árbol de variantes.

### GoBoardControl
- Control personalizado que hereda de `Control` y sobreescribe `Render(DrawingContext)`.
- Capas de dibujo: fondo de madera → líneas y hoshi → coordenadas → piedras (con sombra) → marcadores (último movimiento, etiquetas, formas) → piedra fantasma al pasar el ratón → overlays (territorio, heatmap futuro).
- Escala al tamaño disponible manteniendo proporción cuadrada; usa `RenderTargetBitmap`/cachés para texturas.
- Expone: `BoardState`, `Markers`, `ShowCoordinates`, `LastMove`, `IsInteractive`, evento `PointClicked(Point, MouseButton)`.
- Animación sutil al colocar piedra (opcional, desactivable).

### Servicios de App
- `INavigationService`, `IDialogService`, `ISettingsService` (JSON en carpeta de datos del usuario), `ISoundService` (clic de piedra, captura, aviso de tiempo), `ISecureStore`.
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

# OGS (online-go.com) — resumen de trabajo de la API

> ⚠️ Este resumen es un punto de partida. **Antes de implementar cada parte, verifícala** contra:
> - Código del cliente oficial: https://github.com/online-go/goban (`src/engine/protocol/ClientToServer.ts`, `ServerToClient.ts`, `src/engine/GobanSocket.ts`)
> - Frontend: https://github.com/online-go/online-go.com (`src/lib/sockets.ts`, `src/main.tsx`)
> - Documentación REST: https://apidocs.online-go.com (si está disponible) y el foro https://forums.online-go.com (categoría de desarrollo)
> Si algo difiere, corrige este documento en el mismo commit.
>
> **Última verificación del protocolo WebSocket:** 2026-09-29 contra `online-go/goban` commit `e61c56e` (2026-09-17) y `online-go.com@main`. Las secciones REST y OAuth **aún no** se han verificado.

## Servidores

| Entorno | HTTP | WebSocket |
|---|---|---|
| Producción | `https://online-go.com` | `wss://online-go.com` (rutas alternativas: `wss://wsp.online-go.com`, `wss://wss.online-go.com`) |
| Pruebas | `https://beta.online-go.com` ← usar durante el desarrollo | `wss://beta.online-go.com` |

- El socket se abre en la **raíz del host**, sin ruta (`/socket` no existe). El cliente web deriva la URL cambiando `http`→`ws` del origen.
- Configurable en `appsettings.json` → `Ogs:BaseUrl` (y opcional `Ogs:WebSocketUrl`).

## 1. Autenticación

1. Registrar la aplicación en `https://online-go.com/oauth2/applications/` (y en beta por separado) para obtener `client_id` (y `client_secret` si el tipo de cliente lo requiere). Para una app de escritorio, preferir cliente **público** con **Authorization Code + PKCE** y redirect a `http://127.0.0.1:<puerto>/callback` (loopback). Si OGS no admite PKCE/loopback para la app registrada, usar el grant `password` como alternativa temporal (documentarlo). *(Pendiente de verificar en Fase 4.)*
2. Token: `POST /oauth2/token/` → `access_token`, `refresh_token`, `expires_in`.
3. Peticiones REST: cabecera `Authorization: Bearer <access_token>`.
4. Para el WebSocket se necesita el **JWT de usuario**: `GET /api/v1/ui/config` → campo **`user_jwt`** ✅ (confirmado: el cliente web lo guarda como `config.user_jwt`). Invitado: `jwt: ""`.
5. El servidor puede **rotar el JWT** con el evento `user/jwt` (payload: el nuevo JWT como string). Hay que guardarlo en memoria y usarlo en la siguiente re-autenticación. Nunca registrarlo en logs.
6. Guardar solo `refresh_token` en `ISecureStore`; renovar el access token antes de expirar.

## 2. REST (prefijo `/api/v1/`) — endpoints que usaremos (sin verificar)

| Método | Ruta | Uso |
|---|---|---|
| GET | `me/` | Usuario actual (id, username, ranking) |
| GET | `ui/config` | Config + `user_jwt` para el socket |
| GET | `players/{id}/` | Perfil de jugador |
| GET | `players/{id}/games/?ended__isnull=true` | Partidas activas (verificar filtros) |
| GET | `games/{id}/` | Datos de la partida |
| GET | `games/{id}/sgf` | SGF de la partida (para revisión / guardar) |
| GET | `challenges/` | Desafíos abiertos |
| POST | `challenges/` | Crear desafío abierto |
| POST | `challenges/{id}/accept` | Aceptar desafío |
| DELETE | `challenges/{id}` | Cancelar desafío propio |
| POST | `players/{id}/challenge/` | Desafiar a un jugador concreto |

Respuestas paginadas: `{ count, next, previous, results: [...] }`.

## 3. Tiempo real (WebSocket)

**No es socket.io**: WebSocket plano con mensajes JSON (un array por frame).

- Envío: `[command]`, `[command, data]` o `[command, data, requestId]` (`requestId` entero incremental).
- Recepción: evento `[eventName, data]` o respuesta `[requestId, data, error?]` (el primer elemento es **número** en las respuestas).
- Al desconectarse, las peticiones con id pendientes deben rechazarse.
- Mientras no hay conexión, los envíos se **encolan** y se vacían tras reconectar y re-autenticar.

### Ciclo de conexión
1. Conectar.
2. Enviar `authenticate` (inmediatamente al abrir, **en cada reconexión**):
   ```json
   ["authenticate", {
     "jwt": "<user_jwt o \"\">",
     "device_id": "<uuid estable por instalación>",
     "user_agent": "Hoshi/0.1 (.NET; <SO>)",
     "language": "es",
     "client": "hoshi",
     "client_version": "0.1.0"
   }, 1]
   ```
   Campos opcionales adicionales: `language_version`, `bot_username`, `bot_apikey` (este último **no** lo usamos). Respuesta: `{ id, username }` o `undefined` (invitado).
3. Ping: `["net/ping", { "client": <epoch_ms>, "drift": <ms>, "latency": <ms> }]` cada **10 s** (por defecto). Respuesta `net/pong { client, server }`:
   - `latency = now − client`
   - `drift = now − latency/2 − server`
   - El cliente web usa `timeout_delay = 8000` ms (si no llega el pong → considerar la conexión caída) y adapta el intervalo entre 3 s y 15 s según latencia; tras un timeout duplica ambos valores.
4. Reconexión (comportamiento del cliente oficial):
   - Intentos casi inmediatos con jitter: 1.º a 50 ms, 2.º entre 100–300 ms, después cada 250–750 ms indefinidamente.
   - **No reconectar** si el cierre es `1014` (bad gateway) o `1015` (TLS): error irrecuperable, avisar al usuario.
   - Si llega un mensaje que no es JSON válido, cerrar con código `4000` y reconectar (como mucho una vez cada 60 s) para obtener estado fresco.
   - Tras reconectar: re-autenticar y volver a enviar `game/connect` de cada partida abierta (el servidor reenvía `gamedata`).
   - Hoshi puede añadir un tope razonable (p. ej. 10 s) para no saturar si el servidor está caído; documentarlo en el código.

### Eventos globales relevantes
| Evento | Datos | Notas |
|---|---|---|
| `net/pong` | `{ client, server }` | Ver arriba |
| `user/jwt` | `string` | Nuevo JWT |
| `user/update` | `User` | Datos del usuario actual u otros |
| `HUP` | — | El servidor pide recargar: reconectar y refrescar config |
| `ERROR` | `string \| { errcode }` | Error genérico del servidor |
| `active_game` | `GameListEntry` | Cambios en tus partidas activas (turno, etc.) |
| `notification` | `{ id, type, ... }` | Desafíos recibidos, fin de partida… |

### Partidas — cliente → servidor
| Mensaje | Datos | Notas |
|---|---|---|
| `game/connect` | `{ game_id, chat?: bool }` | Suscribirse |
| `game/disconnect` | `{ game_id }` | Desuscribirse |
| `game/move` | `{ game_id, move: "dd", blur?: ms, clock?: JGOFPlayerClock }` | Letras SGF; pasar = `".."` |
| `game/resign` | `{ game_id }` | |
| `game/cancel` | `{ game_id }` | Cancelar al inicio |
| `game/undo/request` | `{ game_id, move_number }` | `move_number` actual |
| `game/undo/accept` | `{ game_id, move_number }` | |
| `game/undo/cancel` | `{ game_id, move_number }` | |
| `game/pause` / `game/resume` | `{ game_id }` | |
| `game/removed_stones/set` | `{ game_id, removed: bool, stones: "ddee…", needs_sealing?: [...] }` | Intersecciones vacías = dame |
| `game/removed_stones/accept` | `{ game_id, stones: "<todas las removidas>", strict_seki_mode: false }` | `strict_seki_mode` siempre `false` |
| `game/removed_stones/reject` | `{ game_id }` | Vuelve a fase de juego |
| `game/chat` | `{ game_id, body, type: "main"\|"malkovich"\|"moderator"\|"hidden"\|"personal", move_number }` | |
| `game/latency` | `{ game_id, latency }` | Informar latencia al oponente |

### Partidas — servidor → cliente
| Evento | Datos | Notas |
|---|---|---|
| `game/{id}/gamedata` | `GobanEngineConfig` | Estado completo (jugadores, reglas, tamaño, handicap, komi, `moves`, `time_control`, `phase`, `initial_state`…) |
| `game/{id}/move` | `{ game_id, move_number, move: [x, y, Δt?, color?, extra?] }` | `move` es `AdHocPackedMove`; pasar = `[-1, -1]`. `move_number` = número desde el que se jugó |
| `game/{id}/clock` | `GameClock` | Ver §4 |
| `game/{id}/phase` | `"play" \| "stone removal" \| "finished"` | |
| `game/{id}/removed_stones` | `{ removed, stones, all_removed }` o `{ strict_seki_mode }` | |
| `game/{id}/removed_stones_accepted` | `{ player_id, stones, players, phase, score, winner, outcome, end_time }` | Fin del conteo |
| `game/{id}/undo_requested` | `number \| { move_number, requested_by?, undo_move_count? }` | Aceptar ambas formas |
| `game/{id}/undo_accepted` | `number \| { move_number, undo_move_count? }` | |
| `game/{id}/undo_canceled` | `number` | |
| `game/{id}/chat` | `{ channel, line: { chat_id, body, date, move_number, player_id, username?, … } }` | `channel`: `main`/`spectator`/`malkovich`/… |
| `game/{id}/chat/remove`, `game/{id}/reset-chats` | | |
| `game/{id}/player_update` | `{ players: { black, white }, rengo_teams }` | Rengo |
| `game/{id}/latency` | `{ player_id, latency }` | |
| `game/{id}/auto_resign` / `clear_auto_resign` | `{ game_id, player_id, expiration }` | Oponente desconectado |
| `game/{id}/error` | `string` | Jugada rechazada, etc. → revertir jugada pendiente |

### Desafíos
- `challenge/keepalive { challenge_id, game_id }` — **obligatorio ~1 vez por segundo** mientras esperas que acepten un desafío *live/blitz*; si no, el servidor lo cancela.

### Automatch
| Dir. | Mensaje | Datos |
|---|---|---|
| → | `automatch/find_match` | `{ uuid, size_speed_options: [{ size: "9x9"\|"13x13"\|"19x19", speed: "blitz"\|"rapid"\|"live"\|"correspondence", system: "fischer"\|"byoyomi" }], lower_rank_diff, upper_rank_diff, rules: { condition, value }, handicap: { condition, value: "enabled"\|"disabled" } }` (`condition`: `required`\|`preferred`\|`no-preference`; `rules.value`: `japanese`\|`chinese`\|`aga`\|`korean`\|`nz`\|`ing`). **No hay campo `time_control`.** |
| → | `automatch/cancel` | `{ uuid }` |
| → | `automatch/list` | `{}` |
| ← | `automatch/start` | `{ uuid, game_id }` → abrir la partida |
| ← | `automatch/entry` | `AutomatchPreferences` |
| ← | `automatch/cancel` | `{ uuid \| null }` (`null` = todas) |

### Seek graph (desafíos abiertos en vivo)
| → | `seek_graph/connect` / `seek_graph/disconnect` | `{ channel: "global" }` |
| ← | `seekgraph/global` | Array de mensajes: desafío nuevo, `{ challenge_id, delete: true }` o `{ challenge_id, game_started: true }` |

## 4. Reloj

`game/{id}/clock` (`GameClock`):
```
{ game_id, current_player, black_player_id, white_player_id,
  expiration, last_move, now?, paused_since?, start_mode?,
  black_time, white_time, pause?: { paused, paused_since, pause_control } }
```
- `black_time` / `white_time` dependen del sistema:
  - `simple`: número
  - `absolute`: `{ thinking_time }`
  - `fischer`: `{ thinking_time, skip_bonus }`
  - `byoyomi`: `{ thinking_time, periods, period_time, period_time_left? }`
  - `canadian`: `{ thinking_time, moves_left, block_time }`
  - `none`: ausente
- Timestamps (`now`, `last_move`, `expiration`) son epoch en ms del servidor; corregir con `drift` y `latency` del ping. **Unidades de `thinking_time` a confirmar con un fixture real de beta.**
- En correspondencia (días por jugada) no mostrar segundos.

## 5. Reglas de uso responsable

- Respetar límites de peticiones; no hacer polling REST si hay evento por WebSocket.
- Identificar el cliente en `user_agent` y `client`/`client_version`.
- No automatizar jugadas en cuentas humanas.
- Probar siempre en beta.

## 6. Tests

- Grabar mensajes reales de beta como fixtures JSON en `tests/Hoshi.Ogs.Tests/Fixtures/`, anonimizando nombres e ids.
- `FakeOgsSocket` para reproducir secuencias (conectar → gamedata → moves → phase → finished).

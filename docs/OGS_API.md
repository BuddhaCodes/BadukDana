# OGS (online-go.com) — resumen de trabajo de la API

> ⚠️ Este resumen es un punto de partida. **Antes de implementar cada parte, verifícala** contra:
> - Código del cliente oficial: https://github.com/online-go/goban (`src/engine/protocol/ClientToServer.ts`, `ServerToClient.ts`, `src/engine/GobanSocket.ts`)
> - Frontend: https://github.com/online-go/online-go.com (`src/lib/sockets.ts`, `src/main.tsx`)
> - Documentación REST: https://apidocs.online-go.com (si está disponible) y el foro https://forums.online-go.com (categoría de desarrollo)
> Si algo difiere, corrige este documento en el mismo commit.
>
> **Última verificación del protocolo WebSocket:** 2026-09-29 contra `online-go/goban` commit `e61c56e` (2026-09-17) y `online-go.com@main`. REST y login web verificados contra `online-go.com@main` (2026-09-29). OAuth: rutas estándar de django-oauth-toolkit; el soporte de PKCE en cliente público **se confirma en la primera prueba real** (ver §1.1).

## Servidores

| Entorno | HTTP | WebSocket |
|---|---|---|
| Producción | `https://online-go.com` | `wss://online-go.com` (rutas alternativas: `wss://wsp.online-go.com`, `wss://wss.online-go.com`) |
| Pruebas | `https://beta.online-go.com` ← usar durante el desarrollo | `wss://beta.online-go.com` |

- El socket se abre en la **raíz del host**, sin ruta (`/socket` no existe). El cliente web deriva la URL cambiando `http`→`ws` del origen.
- Configurable en `appsettings.json` → `Ogs:BaseUrl` (y opcional `Ogs:WebSocketUrl`).

## 1. Autenticación

Hoshi usa dos modos, elegidos por configuración (`Ogs:AuthMode`):

| Servidor | Modo | Motivo |
|---|---|---|
| `online-go.com` | `OAuth` (authorization code + PKCE) | Único modo permitido en producción (`ReadOptions` rechaza `Password` contra producción). |
| `beta.online-go.com` | `Password` (login web) | **Beta no permite registrar aplicaciones OAuth** (comprobado 2026-09-29). Solo pruebas. |

### 1.1 OAuth (producción)
- Aplicación registrada en `https://online-go.com/oauth2/applications/`: **Client type = Public**, **Authorization grant = Authorization code**, redirect `http://127.0.0.1:8734/callback` (loopback IPv4, RFC 8252; no `localhost`). Sin `client_secret`. El `client_id` no es secreto y va en `appsettings.json`.
- `GET /oauth2/authorize/?response_type=code&client_id=…&redirect_uri=…&code_challenge=<S256>&code_challenge_method=S256&state=…` en el navegador del sistema.
- `LoopbackRedirectListener` escucha solo en `127.0.0.1:<puerto>`, valida `state`, devuelve 404 a otras rutas y muestra una página "puedes cerrar esta pestaña".
- `POST /oauth2/token/` (form) con `grant_type=authorization_code`, `code`, `redirect_uri`, `client_id`, `code_verifier` → `access_token`, `refresh_token`, `expires_in`.
- Renovación: `grant_type=refresh_token` cuando faltan < 60 s para expirar. Logout: `POST /oauth2/revoke_token/` y borrar el token local.
- REST: `Authorization: Bearer <access_token>`.
- ⚠️ Sin verificar hasta la primera prueba: que OGS acepte PKCE sin secreto y que `ui/config` devuelva `user_jwt` con Bearer.

- **Botón «Continuar con Google»:** abre `GET /login/google-oauth2/?next=<ruta relativa de /oauth2/authorize/?…>` (ruta del login social de OGS, python-social-auth; verificada en `SocialLoginButtons.tsx`). Tras Google, OGS redirige a `next`, el usuario autoriza a Hoshi y el código llega al listener loopback como en el flujo normal. `next` debe ser relativo (social-auth rechaza otros hosts). Otras rutas: `/login/facebook/`, `/login/github/`, `/login/apple-id/` (`OgsLoginProvider`).
- Hoshi **no** habla con Google ni recibe tokens de Google: la API de OGS no los acepta y exigiría un secreto en la app.

### 1.2 Login web (beta, desarrollo)
- `GET /api/v1/ui/config` para obtener la cookie `csrftoken`.
- `POST /api/v0/login` JSON `{ username, password, ebi, timezone }` con `X-CSRFToken` y `Referer`; la respuesta es la misma estructura que `ui/config` (usuario + `user_jwt`) y deja la cookie de sesión.
- Las peticiones siguientes usan esas cookies (+ CSRF en escrituras). Las cookies las gestiona `OgsAuthService` (el `HttpClient` tiene `UseCookies=false`).
- La contraseña se envía una vez y se borra del view model inmediatamente; nunca se guarda ni se registra.
- Beta no admite apps OAuth, así que ahí no hay botón de Google. Una cuenta solo-Google puede ponerse contraseña en Configuración → Cuenta (`password_is_set: false` permite fijarla sin la anterior), o usar una cuenta de pruebas.

### 1.3 JWT del WebSocket
- `user_jwt` de `ui/config` (o de la respuesta del login). Invitado: `jwt: ""`.
- El servidor puede rotarlo con `user/jwt` (payload: string) → `OgsAuthService.UpdateJwt`. Nunca en logs.

### 1.4 Almacenamiento
- Solo el `refresh_token` (clave `ogs.refresh_token`) en el almacén del SO: DPAPI (Windows, archivo cifrado en `secrets/`), Keychain (macOS, `security -i` por stdin), Secret Service (Linux, `secret-tool` por stdin). Si no hay almacén, se guarda en memoria y se avisa en el log.

## 2. REST (prefijo `/api/v1/`) — usados por Hoshi (verificados contra el cliente web)

| Método | Ruta | Uso |
|---|---|---|
| GET | `ui/config` | Usuario + `user_jwt` |
| GET | `ui/overview` | `active_games[]` (cada una con `black`, `white`, `json.player_to_move`, `json.phase`) |
| GET | `players?username=<nombre>` | Buscar jugador exacto → `results[0]` |
| POST | `challenges` | Crear desafío abierto |
| POST | `players/{id}/challenge` | Desafío directo |
| POST | `challenges/{id}/accept` | Aceptar (respuesta: forma exacta sin confirmar; se lee `game` o `game_id`) |
| DELETE | `me/challenges/{id}` | Cancelar desafío propio |

Cuerpo de creación (igual que `ChallengeModal` del web): `{ initialized:false, challenger_color, invite_only, min_ranking, max_ranking, rengo_auto_start:0, game:{ name, rules, ranked:false, width, height, handicap, komi_auto:"automatic"|"custom", komi?, disable_analysis, initial_state:null, private, time_control, time_control_parameters, pause_on_weekends } }`. Respuesta: `{ challenge, game }`.

- **Hoshi siempre envía `ranked: false`** y no permite aceptar desafíos clasificatorios.
- Velocidad (como goban): media por jugada = `main/moves + period` con `moves = round(0.7·w·h)/2`; `< 10 s` blitz, `≤ 3600 s` live, resto (o 0) correspondencia. Live/blitz requieren `challenge/keepalive` cada segundo (§3).
- Pendiente (fases 5–6): `games/{id}`, `games/{id}/sgf`, perfiles.

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

### Partidas — verificado (goban e61c56e, 2026-09-29) e implementado en `OgsGameSession`
- `gamedata`: `players.{black,white}` (`id`, `username`, `rank`), `black_player_id`/`white_player_id`, `width`/`height`, `rules`, `komi`, `handicap`, `free_handicap_placement`, `initial_state {black,white}` (letras SGF), `initial_player`, `moves` (arrays `[x,y,Δt,color?]`, pasar `[-1,-1]`), `phase`, `time_control` (JGOF, segundos), `clock`, `removed`, y al terminar `winner` (id o `"black"`/`"white"`) + `outcome` (`"Resignation"`, `"Timeout"`, `"3.5 points"`…).
- Colores: `initial_player` y alternancia; con `free_handicap_placement` negras juegan las primeras `handicap` jugadas (los pases implícitos durante la colocación se ignoran), como `GobanEngine`.
- `game/{id}/move`: goban ignora `move_number` y añade la jugada a la última oficial; Hoshi hace lo mismo (lo registra en el log). El servidor devuelve también **la jugada propia**: Hoshi no coloca la piedra hasta ese eco.
- Abandono / tiempo: llegan como un `gamedata` nuevo con `phase: "finished"`; el conteo termina con `removed_stones_accepted` (`phase: "finished"`, `score`, `winner`, `outcome`).
- Reloj: `black_time`/`white_time` = `{thinking_time, periods, period_time}` en **segundos** (número = ms en tiempo simple); `last_move` en ms del servidor. Solo corre el jugador al turno: `elapsed = ahora_servidor − last_move` (o hasta `paused_since` si está en pausa); byo-yomi consume periodos completos (`OgsClockMath`, misma lógica que `computeNewPlayerClock`).
- Chat: `game/{id}/chat` `{channel, line:{chat_id, body, date(s), move_number, player_id, username}}`.

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
- Probar en beta. En producción solo partidas **no clasificatorias** y privadas contra una segunda cuenta propia (ver CLAUDE.md).

## 6. Tests

- Grabar mensajes reales de beta como fixtures JSON en `tests/Hoshi.Ogs.Tests/Fixtures/`, anonimizando nombres e ids.
- `FakeOgsSocket` para reproducir secuencias (conectar → gamedata → moves → phase → finished).

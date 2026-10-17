# Otros servidores: KGS, Pandanet IGS y Fox (investigación, 2026-10-17)

> Solo investigación y plan; **no hay código**. Cada servidor espera una decisión del usuario (sección 6).
>
> La búsqueda web general no estaba disponible en esta sesión. Todo sale de páginas oficiales y del código de qGo. Lo marcado *(sin verificar)* no se pudo confirmar en una fuente.

## 1. KGS (gokgs.com)

### Protocolo

KGS tiene una **pasarela JSON sobre HTTP** (webapp «kgs-json») con un único servlet, `/access`.

- **POST** envía un mensaje por petición.
- **GET** hace *long polling*: queda abierto hasta que hay mensajes o pasan unos 60 s, y devuelve `{messages:[…]}`.
- Siempre tiene que haber un GET pendiente.
- Un código distinto de 200 o un mensaje `LOGOUT` cierran la sesión.

El webapp se distribuye como `.war` (Java 8, Tomcat/Jetty). gokgs.com expone una demo en `/json/access`. *(Sin verificar)* si un cliente de escritorio puede usar ese endpoint público.

**Login:** `LOGIN {name, password, locale}`. Solo contraseña, sin OAuth; la sesión va con cookie. Si llega `RECONNECT` (la cuenta entró desde otro sitio), **no** hay que reconectar solo.

**Modelo:** todo es un canal con `channelId` (salas, partidas, desafíos, archivos de usuario).

| Uso | Mensajes |
|---|---|
| Listas | `GLOBAL_LIST_JOIN_REQUEST` (CHALLENGES/ACTIVES/FANS) |
| Desafíos | `CHALLENGE_CREATE`, `CHALLENGE_SUBMIT`, `CHALLENGE_ACCEPT` |
| Juego | `GAME_MOVE {loc: "PASS" \| {x, y}}`, `GAME_RESIGN` |
| Conteo | `GAME_MARK_LIFE`, `GAME_SCORING_DONE {doneId}` |
| Chat | `CHAT` |
| SGF | Llegan como `sgfEvents`, que encajan directamente con Hoshi.Sgf |

Queda por leer con detalle el reloj y el estado (`dataTypes.html`).

### Términos

- El ToS prohíbe «*modified or otherwise unauthorized clients*».
- A la vez, KGS publica el protocolo, tiene el grupo «kgs-client-coding» y hay clientes de terceros (Shin KGS, GoUniverse).
- **Hay que pedir permiso escrito** a wms (wms@igoweb.org) o a admin@gokgs.com antes de publicar.
- Los bots necesitan aprobación de un administrador.

## 2. Pandanet IGS

### Protocolo

- Texto por TCP (telnet). *(Sin verificar)* el host y los puertos: igs.joyjoy.net, 6969 o 7777.
- Login con los prompts `Login:` y `Password:`. *(Sin verificar)* que exista TLS; si no lo hay, **la contraseña viaja en claro**.
- Tras entrar: `toggle client on`, `id <cliente>`, `toggle newundo on`, `toggle nmatch on`, `toggle seek on`. Se mantiene viva la sesión con `ayt` cada 10 minutos.
- Cada línea del servidor empieza por un código numérico:

| Código | Significado |
|---|---|
| 1 | prompt |
| 5 | error |
| 7 | games |
| 9 | info |
| 15 | jugada |
| 19 | say |
| 20 | puntuación |
| 22 | tablero |
| 24 | tell |
| 49 | piedras retiradas en el conteo |
| 51 | chat de la partida |
| 63 | seek |

- **Comandos:** `observe`, `match`/`nmatch`, `D16 <id>`, `pass`, `resign`, `done`, `say`, `kibitz`, `moves <id>`.
- **SGF:** se reconstruye a partir de `moves`.
- **Referencia:** qGo (GPL-2.0, `src/network/igsconnection.cpp`). Es GPL: se puede leer como documentación, **no copiar** a Hoshi.

### Términos

- El ToS no habla de clientes de terceros. La FAQ dice que existen «*clients which are not supported by Pandanet*»: se toleran, pero sin soporte.
- Se cierran cuentas por «cheating».
- Contacto: gopanda2@pandanet-igs.com.

## 3. Fox Weiqi (野狐围棋)

- **Protocolo propietario y sin documentar.** No hay API pública ni programa para desarrolladores.
- La única vía «oficial» para programas es un paquete para conectar motores de IA como cuentas de IA (ai@foxwq.com). *(Sin verificar)* qué expone y en qué condiciones.
- **Herramientas de terceros:** las que descargan SGF de Fox (FoxRequest) usan llamadas no documentadas. Las de «sincronizar con un clic» (readboard) leen la pantalla, que es justo lo que se usa para hacer trampa con IA. Hoshi no debe imitar ninguna de las dos.
- **Conclusión:** jugar en Fox desde Hoshi exigiría ingeniería inversa de un protocolo privado, con riesgo alto de incumplir el ToS y de bloqueos de cuenta. Como mucho, se podría **importar SGF** si Fox lo autoriza.

## 4. Comparativa

| | KGS | IGS | Fox |
|---|---|---|---|
| Transporte | JSON por HTTP con long polling | TCP de texto | Propietario |
| Documentación | Oficial | Ayuda clásica + qGo | Ninguna |
| Autenticación | Contraseña (dentro de HTTPS) | Contraseña, posiblemente en claro | Desconocida |
| Clientes de terceros | Pedir permiso | Tolerados, sin soporte | No |
| SGF | Nativo | Reconstruir desde `moves` | Solo visor |
| Servidor de pruebas | No | No | No |
| Esfuerzo | **M** | **M–L** | **No recomendado** |

## 5. Plan para Hoshi

**0. Común (S).** Extraer `IGoServerClient` y `ServerCapabilities` (OAuth, contraseña, automatch, revisión…) a partir de `Hoshi.Ogs`. Cada adaptador emite los mismos eventos de dominio: jugada, reloj, fase, chat, conteo y fin de partida. `Hoshi.App` no cambia.

**1. `Hoshi.Kgs` (M).**
1. Pedir permiso y aclarar qué endpoint usar. No conviene alojar una pasarela propia: las contraseñas pasarían por nuestro servidor.
2. Transporte: `HttpClient` con cookies, un bucle GET siempre activo y una cola de POST. Cancelable, con backoff y sin reconexión automática tras `RECONNECT`.
3. DTOs con source generators. Fixtures grabados con una cuenta de pruebas en partidas libres.
4. Mapeo: `sgfEvents` a Core/Sgf, `GAME_STATE` a reloj y fase, `GAME_MARK_LIFE` a conteo.
5. UI: listas de desafíos y partidas activas, observar, archivo del usuario.

**2. `Hoshi.Igs` (M–L).**
1. Confirmar si hay TLS. Si no, avisar de la contraseña en claro o descartar el servidor.
2. Transporte: `TcpClient` con lector de líneas, máquina de estados del login y `ayt`.
3. Un parser por código, probado con transcripciones grabadas.
4. Desafíos, jugadas, conteo y chat. SGF reconstruido desde `moves`.

**3. Fox:** no implementar el juego en línea.

**Contraseñas.** Ninguno de los tres ofrece OAuth. Propuesta:
- **Por defecto:** pedir la contraseña en cada sesión, mantenerla solo en memoria y descartarla tras el login.
- **Opcional:** «Recordar en el almacén seguro del sistema», con consentimiento explícito, por servidor y revocable. Esto **cambia la regla actual** («solo tokens OAuth»): hace falta la decisión del usuario.
- Nunca registrar `LOGIN` ni `Password:` en los logs.

**Juego limpio.** KataGo y los motores se cierran durante las partidas en vivo del jugador en **cualquier** servidor, como ya ocurre con OGS. KGS habla de «dishonest play» e IGS de «cheating». Que Hoshi sea más estricto que ellos es seguro.

## 6. Decisiones pendientes del usuario

1. ¿Escribimos a KGS y a Pandanet para pedir autorización antes de programar?
2. ¿Se acepta guardar contraseñas en el almacén seguro con consentimiento, o se piden siempre?
3. Si IGS no tiene TLS, ¿lo soportamos con aviso o lo descartamos?
4. ¿Interesa importar SGF de Fox, aunque sea un uso no oficial?

## Fuentes

### KGS

- https://www.gokgs.com/json/protocol.html
- https://www.gokgs.com/json/upstream.html
- https://www.gokgs.com/json/downstream.html
- https://www.gokgs.com/tos.jsp
- https://www.gokgs.com/help/faq/bots.html

### Pandanet IGS

- https://pandanet-igs.com/communities/pandanet/411
- https://pandanet-igs.com/communities/pandanet/11
- https://github.com/pzorin/qgo

### Fox

- https://www.foxwq.com/soft/aiprogramandmanual.html
- https://github.com/yzyray/FoxRequest
- https://github.com/yzyray/readboard

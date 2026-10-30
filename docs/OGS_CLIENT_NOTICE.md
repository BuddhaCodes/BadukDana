# Presentar Hoshi a OGS como cliente

> Borrador preparado el 2026-10-30. Lo envía el usuario desde su cuenta de OGS; Claude no publica nada en su nombre.

## Por qué

Hoshi ya no es una compilación de desarrollo: tiene instaladores públicos, actualizaciones automáticas y automatch (que en OGS siempre es clasificatorio). Conviene que el equipo de OGS sepa que existe, cómo se identifica y qué normas de juego limpio aplica, **antes** de que lleguen muchas partidas clasificatorias desde Hoshi. Además, es la condición que pusimos para abrir algún día los desafíos clasificatorios (CLAUDE.md §6).

## Dónde enviarlo (por orden)

1. **Foro de OGS, categoría de desarrollo** (`forums.online-go.com`, categoría *Development* / *OGS Development*). Es público, lo leen los desarrolladores y la comunidad, y deja constancia escrita. Comprueba el nombre exacto de la categoría al publicar.
2. **Mensaje directo a un moderador o a anoek** (desarrollador principal) en el mismo foro, enlazando el hilo, si en una semana nadie responde.
3. **Opcional:** abrir una *discussion* en `github.com/online-go/online-go.com` solo si en el foro piden detalles técnicos.

Adjunta: enlace al repositorio, a la web y el *client id* de la aplicación OAuth (es público).

## Qué decir (en inglés)

> **Title:** Hoshi — a free, open-source desktop & Android client for OGS
>
> Hi everyone,
>
> I'm the developer of **Hoshi** (https://github.com/BuddhaCodes/BadukDana · https://buddhacodes.github.io/BadukDana/), a free and open-source Go app for Windows, macOS, Linux and Android. Besides local study with KataGo, it lets players sign in to OGS and play.
>
> I'd like to introduce it properly and make sure it plays by the rules:
>
> - **Sign-in:** OAuth 2 authorization code + PKCE as a public client (client id `zoteadioQlEtBHNRuOSBQ33UdPBEo5MOICYTOiB1`). Passwords are never stored; the password form only exists for beta.online-go.com.
> - **Identification:** every REST and WebSocket request sends `User-Agent: Hoshi/<version>`.
> - **Protocol:** the plain JSON WebSocket, following goban's `protocol` definitions (keepalive, `net/ping`, reconnection with backoff).
> - **Fair play:** during the player's live OGS games Hoshi switches off every engine (KataGo or any GTP engine), analysis, territory from engine ownership, joseki hints and the joseki trainer. Only a simple non-AI territory estimate and the move-by-move study notes remain. Engines come back only after the game ends.
> - **Ranked play:** challenges created in Hoshi are always unranked, and ranked open challenges can't be accepted. Automatch is offered, with a clear notice that OGS ranks those games.
> - **No bots:** Hoshi never plays moves on its own.
>
> Questions for you:
>
> 1. Is there anything you'd like a third-party client to do differently (rate limits, identification, endpoints to avoid)?
> 2. Are you comfortable with Hoshi offering Automatch, and, later on, letting players create and accept ranked challenges under the fair-play rules above?
> 3. Is there a preferred contact for API changes, so that Hoshi doesn't break your servers when something changes?
>
> Thanks for OGS and for goban, which made this possible.

## Después de la respuesta

- Anota la respuesta y la fecha en este archivo y en `docs/OGS_API.md`.
- Si OGS acepta los desafíos clasificatorios: quitar el bloqueo de `CanAccept` en `LobbyViewModel`, permitir `ranked` en `ChallengeRequest` y actualizar CLAUDE.md §6, el README y la web. Es una decisión del usuario.
- Si OGS pide cambios: crear tareas en `docs/ROADMAP.md` antes de la próxima versión.

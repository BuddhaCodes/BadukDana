# CLAUDE.md — Proyecto "Hoshi" (cliente de Baduk/Go en Avalonia)

> Archivo de contexto para Claude. Léelo completo al inicio de cada sesión, junto con los documentos de `docs/`.
> Nombre provisional del proyecto: **Hoshi**. Cámbialo si el equipo decide otro.

## 1. Qué estamos construyendo

Una aplicación de escritorio multiplataforma (Windows, macOS, Linux) para jugar y estudiar Baduk (Go/Weiqi):

1. **Tablero y editor SGF local** con estética inspirada en [Sabaki](https://github.com/SabakiHQ/Sabaki): tablero de madera, piedras con textura, interfaz oscura y minimalista, árbol de variantes, panel de comentarios.
2. **Juego en línea mediante la API de OGS** ([online-go.com](https://online-go.com)): iniciar sesión, buscar/aceptar desafíos, automatch, jugar partidas en tiempo real con reloj, chat, fase de conteo y revisión de partidas terminadas.
3. **Juego local** entre dos personas en el mismo equipo (y, en una fase posterior, contra motores GTP como KataGo).

No es un port de Sabaki (Sabaki es Electron + Preact). Tomamos su **estética y su UX**, no su código.

## 2. Stack técnico (decidido)

| Área | Elección |
|---|---|
| Runtime | .NET 10 LTS (C# 14), `Nullable` y `TreatWarningsAsErrors` activados *(migrado desde .NET 9 el 2026-09-29)* |
| UI | Avalonia UI 11.x, tema Fluent en modo oscuro como base + estilos propios |
| MVVM | CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`) |
| DI / hosting | Microsoft.Extensions.DependencyInjection + Hosting |
| HTTP | `HttpClient` vía `IHttpClientFactory`, System.Text.Json (source generators) |
| Tiempo real OGS | `System.Net.WebSockets.ClientWebSocket` (protocolo JSON propio de OGS, ver `docs/OGS_API.md`) |
| Credenciales | Almacenamiento seguro del SO (DPAPI / Keychain / libsecret) detrás de `ISecureStore` |
| Logs | Microsoft.Extensions.Logging + Serilog a archivo |
| Tests | xUnit + FluentAssertions **7.x** (la 8.x tiene licencia comercial); Avalonia.Headless para pruebas de UI |

Versiones de paquetes centralizadas en `Directory.Packages.props`.

Si crees que una elección debe cambiar, **propónlo y espera confirmación**; no la cambies por tu cuenta.

## 3. Estructura de la solución

```
Hoshi.sln
src/
  Hoshi.Core/        # Reglas de Go puras: tablero, capturas, ko, superko, conteo. Sin dependencias de UI ni red.
  Hoshi.Sgf/         # Parser/serializer SGF (FF[4]), árbol de juego, propiedades, GameCursor.
  Hoshi.Ogs/         # Cliente OGS: auth, REST, WebSocket, DTOs, mapeo a modelos de Core. Emite eventos de dominio.
  Hoshi.Engines/     # Cliente del motor de análisis de KataGo (JSON); más adelante GTP para jugar.
  Hoshi.App/         # Avalonia: Views, ViewModels, controles, estilos, assets. Une Ogs con Sgf.
tests/
  Hoshi.Core.Tests/
  Hoshi.Sgf.Tests/
  Hoshi.Ogs.Tests/   # Con fixtures JSON grabados; nunca contra el servidor real en CI.
  Hoshi.Engines.Tests/ # Con un proceso KataGo simulado; nunca KataGo real en CI.
  Hoshi.App.Tests/   # Incluye ArchitectureTests, que hacen cumplir la regla de dependencias.
docs/
```

Regla de dependencias: `App → Ogs, Sgf, Engines, Core` · `Ogs → Core` · `Sgf → Core` · `Engines → Core` · `Core → (nada)`.

`Hoshi.Ogs` **no** conoce `GameCursor` ni el árbol SGF: expone eventos (jugada recibida, reloj, fase, chat…) con modelos de Core, y es `Hoshi.App` quien los aplica al cursor (decisión 2026-09-29, ver `docs/ARCHITECTURE.md`).

## 4. Convenciones

- Código, identificadores, commits y comentarios en **inglés**. Documentación y conversación con el usuario en **español**.
- Coordenadas internas: `Point(X, Y)` con origen (0,0) arriba-izquierda. Conversión explícita a SGF (`"dd"`), a OGS (también letras SGF; pasar = `".."`) y a notación humana (`"D16"`, sin la letra I).
- El modelo de juego (`Hoshi.Core`) es **inmutable**: cada jugada produce un `BoardState` nuevo. Esto simplifica el árbol de variantes y el deshacer.
- Nada de lógica de negocio en code-behind. El control del tablero (`GoBoardControl`) solo dibuja y emite eventos.
- Toda llamada de red es `async`, cancelable (`CancellationToken`) y con manejo de reconexión.
- Nunca registrar tokens, contraseñas ni JWT en logs.
- Commits pequeños con Conventional Commits (`feat:`, `fix:`, `refactor:`...).

## 5. Cómo trabajar conmigo (Claude)

1. **Antes de programar una funcionalidad**, revisa `docs/ROADMAP.md` y confirma en qué fase/tarea estamos.
2. Para cualquier cosa de OGS, **verifica el protocolo contra el código fuente oficial** (`github.com/online-go/goban`, carpeta `src/engine/protocol/` y `GobanSocket`) o la documentación de la API antes de implementar. `docs/OGS_API.md` es un resumen de trabajo y puede estar desactualizado; si encuentras discrepancias, actualízalo.
3. Escribe tests primero para `Hoshi.Core` y `Hoshi.Sgf` (reglas y parsing tienen muchos casos borde).
4. Al terminar una tarea: compila (`dotnet build`), ejecuta tests (`dotnet test`), y resume en 2–4 líneas qué cambió y qué queda pendiente.
5. Si una decisión afecta la arquitectura, la UX o la privacidad del usuario, pregunta antes.
6. Cuando pruebes contra OGS real, usa **beta.online-go.com** (servidor de pruebas) con usuario y contraseña (se elige en el selector de servidor del lobby). Como beta no admite aplicaciones OAuth, el flujo OAuth solo puede probarse en online-go.com: ahí, **solo partidas no clasificatorias y privadas contra una segunda cuenta propia**; nunca clasificatorias (Hoshi siempre envía `ranked: false`). *(Actualizado 2026-09-29.)*

## 6. Qué NO hacer

- No copiar código de Sabaki. Sus assets están bajo licencia MIT: el tema Clásico usa `board.png` (Shudan) y `tatami.png` con su aviso en `THIRD_PARTY_NOTICES.md`. Los demás temas usan arte generado por Hoshi, fuentes OFL e iconos MIT/ISC; todo recurso de terceros nuevo debe añadirse a `THIRD_PARTY_NOTICES.md` (y su licencia a `licenses/`) en el mismo commit.
- No usar socket.io: OGS usa ahora un WebSocket JSON plano.
- No implementar "bots" que jueguen automáticamente en OGS sin marcar la cuenta como bot (va contra sus términos).
- No ofrecer análisis de IA ni estimación de territorio al jugador durante sus partidas de OGS en curso (ayuda de motor prohibida por OGS); solo tras terminar o en partidas locales.
- No guardar la contraseña del usuario; solo tokens OAuth en el almacén seguro. El login con contraseña existe solo para beta y está bloqueado contra producción.
- No poner `client_secret` ni ningún secreto en el repositorio: la app OAuth es un cliente público con PKCE.

## 7. Documentos de referencia

- `docs/ARCHITECTURE.md` — capas, modelos principales, flujo de datos.
- `docs/DESIGN.md` — guía visual estilo Sabaki (colores, tablero, piedras, layout).
- `docs/OGS_API.md` — autenticación, REST y WebSocket de OGS.
- `docs/ROADMAP.md` — fases, tareas y criterios de aceptación.

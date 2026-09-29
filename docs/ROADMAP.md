# Roadmap

Cada tarea termina con: compila sin warnings, tests en verde, y una captura (si es visual) comparada con Sabaki.

## Fase 0 — Esqueleto (½ día) ✅ 2026-09-29
- [x] Solución y proyectos según `CLAUDE.md` §3, `Directory.Build.props` con Nullable, warnings como errores, versión de lenguaje.
- [x] App Avalonia vacía con DI, logging y ventana oscura.
- [x] CI (GitHub Actions): build + test en Windows, macOS y Linux. *(workflow escrito; pendiente de primera ejecución en GitHub)*
**Aceptación:** `dotnet run --project src/Hoshi.App` abre una ventana con el fondo `Bg.Window`.

## Fase 1 — Reglas (Hoshi.Core) ✅ 2026-09-29 (111 tests)
- [x] `Point`, `Stone`, `BoardState` inmutable con Zobrist.
- [x] Capturas, suicidio, ko simple, superko posicional (y situacional para AGA/NZ).
- [x] Handicap fijo y libre; komi (por defecto en `RuleSet`, compensación de handicap china/AGA).
- [x] Conteo por territorio y por área con piedras muertas marcadas.
**Aceptación:** ≥ 40 tests, incluyendo: captura múltiple, snapback, ko, triple ko con superko, seki en conteo.

## Fase 2 — Tablero visual ✅ 2026-09-29
- [x] `GoBoardControl` con madera, líneas, hoshi, coordenadas, piedras con textura y sombra (texturas procedurales propias).
- [x] Piedra fantasma, última jugada, marcadores SGF (TR, SQ, CR, MA, LB).
- [x] Juego local 2 jugadores: clic para jugar, pasar (P), deshacer (Ctrl+Z), nueva partida 19/13/9 (Ctrl+N).
**Aceptación:** partida 19×19 local jugable; redimensionar la ventana mantiene el tablero nítido; aspecto cercano a Sabaki.

## Fase 3 — SGF y árbol de variantes ✅ 2026-09-29 (70 tests Sgf + 20 de App)
- [x] Parser/serializer SGF con tests de ida y vuelta (SGF de muestra propio, CC0, en `tests/Hoshi.Sgf.Tests/Samples`).
- [x] `GameCursor` y navegación por teclado (←/→/Home/End/↑/↓) y rueda.
- [x] Panel de árbol de variantes (`GameTreeControl`) y panel de comentarios.
- [x] Abrir/guardar (Ctrl+O / Ctrl+S / Ctrl+Shift+S), arrastrar y soltar `.sgf`, diálogo de info de partida (Ctrl+I).
- [x] Modo edición (Ctrl+E): piedras de colocación, triángulo, cuadrado, círculo, cruz, etiquetas.
**Aceptación:** abrir un SGF con variantes y comentarios, editar, guardar y reabrir sin pérdidas.

## Fase 4 — OGS: sesión y lobby ✅ (código; pendiente la prueba real)
- [x] Login OAuth + PKCE en online-go.com, refresh, logout, almacenamiento seguro (DPAPI / Keychain / secret-tool).
- [x] Selector de servidor en el lobby (online-go.com / beta), independiente del entorno; «Continuar con Google» siempre va a online-go.com; contraseña solo en beta (no admite apps OAuth).
- [x] Cliente WebSocket con autenticación, ping/deriva, reconexión, `user/jwt`.
- [x] Lobby (Ctrl+L): mis partidas activas, desafíos abiertos (seek graph), crear/aceptar/cancelar desafío con keepalive.
**Aceptación:** iniciar sesión en beta (contraseña) y en online-go.com (OAuth), ver partidas activas y crear un desafío no clasificatorio visible desde la web de OGS.

## Fase 5 — OGS: jugar (en curso)
- [x] `OgsGameSession`: gamedata, jugadas, reloj (algoritmo de goban + deriva del socket), fin de partida (abandono, tiempo, conteo).
- [x] Tablero en línea: abrir desde el lobby (al empezar un desafío o con «Abrir» / doble clic en «Mis partidas»), jugar con clic (la piedra aparece al confirmar el servidor), revisar jugadas anteriores sin perder las nuevas.
- [x] Pasar, abandonar (con confirmación), pedir/aceptar deshacer, chat. La tecla P no pasa en partidas en línea.
- [x] Fase de conteo: clic en un grupo para marcarlo muerto o vivo, aceptar o reanudar; resultado en `RE`.
- [x] Salir de la partida deja el árbol como partida local guardable en SGF.
- [ ] Automatch.
- [ ] Notificación (sonido/visual) cuando es tu turno en otra partida.
- [ ] Capturar fixtures reales de beta y ajustar lo que difiera (forma exacta de `move_number`, reloj en fase de conteo).
**Aceptación:** jugar una partida completa en beta contra otra cuenta (navegador) de principio a conteo final, con relojes coincidiendo ±1 s.

## Fase 6 — Pulido
- [ ] Preferencias (tema, sonidos, coordenadas, piedras "fuzzy", servidor).
- [ ] Revisión de partidas OGS terminadas (descargar SGF y abrir en el editor).
- [ ] Empaquetado: instalador Windows (MSIX/Velopack), `.app` macOS, AppImage Linux.
- [ ] Localización es/en.

## Fase 7 (opcional) — Motores
- [ ] Cliente GTP (KataGo): jugar contra motor, análisis con winrate y mapa de calor sobre el tablero.

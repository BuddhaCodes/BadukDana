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
- [x] Temas con identidad propia (Cielo nocturno, Tinta y oro, Jardín zen, Minimal cálido, Clásico), fondos animados, fuentes, iconos, piedras y animaciones; Preferencias (Ctrl+,).
- [ ] Más preferencias (sonidos, coordenadas, piedras "fuzzy", servidor por defecto).
- [ ] Revisión de partidas OGS terminadas (descargar SGF y abrir en el editor).
- [ ] Empaquetado: instalador Windows (MSIX/Velopack), `.app` macOS, AppImage Linux.
- [ ] Localización es/en.

## Fase 7 — Motores y análisis
- [x] Estimación de territorio propia (`TerritoryEstimator`, sin motor): territorio seguro (regiones cerradas) y potencial (influencia), con estimación del resultado. Tecla T / botón «Territorio».
- [x] Cliente del motor de análisis JSON de KataGo (`Hoshi.Engines`), configurable en Preferencias → Análisis.
- [x] Análisis en segundo plano (tecla A / botón «Análisis»): valora la última jugada frente a la mejor de KataGo (mejor · excelente · buena · imprecisa · error · error grave, con puntos perdidos), winrate, ventaja, sugerencias sobre el tablero, gráfica de la partida (clic para navegar) y territorio con el mapa de propiedad de KataGo.
- [x] Desactivado en partidas de OGS en curso (normas de OGS).
- [x] Celebración de jugadas fuertes (2026-09-30): cuando KataGo valora como buena/excelente/la mejor una jugada que acabas de hacer, sonido de explosión cinemática (sintetizado por Hoshi, `tools/gen_sounds.py`) e impacto en el tablero (destello, ondas expansivas, chispas, temblor y, para la mejor, grietas incandescentes). Configurable en Preferencias → Análisis (activar, volumen, sonidos propios).
- [x] Música adaptativa (2026-09-30): lo-fi sintetizado por Hoshi en 5 capas que se van sumando con rachas de buenas jugadas, se enfría con el tiempo y con errores, y hace un «tape-stop» con un error grave. Tecla M y Preferencias → Análisis. Windows (waveOut) y Linux (pacat/aplay); macOS pendiente. Más intensa (2026-09-30): sexta capa «overdrive», golpe al subir de nivel y bombeo tipo sidechain.
- [x] Online (2026-10-01): estimación rápida de territorio permitida; efectos y música según la intensidad de la pelea (`Hoshi.Core.FightMeter`), nunca según KataGo. Lo mismo en local si KataGo no está configurado.
- [x] Biblioteca de partidas jugadas (2026-10-01): `ReplayStore` (SGF + `index.json` en `replays/`), guardado automático (local al cambiar de partida o cerrar; OGS al terminar), ventana «Partidas jugadas» (Ctrl+R); al revisar, cada paso adelante se valora con KataGo y dispara sus efectos.
- [x] Entrenador de josekis (2026-10-01, Ctrl+J): líneas sacadas de SGF (cada hoja = una línea, identidad común a sus 8 simetrías), práctica en una esquina y orientación al azar colocando las piedras de los dos colores, imagen en espejo aceptada, «también es joseki» para otras líneas conocidas, respuesta tras dos fallos, repetición espaciada Leitner (6 cajas: 10 min, 1, 3, 7, 14, 30 días). Biblioteca: línea de inicio propia (invasión 3-3) + importación de SGF + «añadir la línea del tablero principal». Cerrado durante partidas de OGS en curso del jugador.
- [x] Josekis v2 (2026-10-01): el entrenador usa el tablero principal (Ctrl+J; la partida queda intacta debajo, Esc vuelve), lista de líneas con filtro por familia (4-4, 3-4, 3-3, 4-5, 3-5), «Nueva línea de OGS» (paseo aleatorio por jugadas ideales/buenas del Joseki Explorer, guardada en `joseki/ogs-explorer.sgf`). «Josekis naturales» (botón Joseki, tecla J): en cada esquina, las continuaciones conocidas (OGS + biblioteca) como discos de colores y una píldora con la valoración de la última jugada. Pendiente: probar el Joseki Explorer contra el servidor real.
- [x] Josekis v3 (2026-10-01): comentarios de joseki (Joseki Explorer + la línea de tu biblioteca que se sigue o se completa) y leyenda de colores en la barra lateral, sobre el comentario. Página promocional: sección Joseki, cuatro funciones nuevas y fuentes propias servidas con la página (Fraunces, Figtree, JetBrains Mono; Shippori Mincho B1 para 星/正), sin Google Fonts.
- [x] KataGo por defecto (2026-10-02): `KataGoLocator` (Preferencias → incluido junto a la app → instalado por Hoshi → del sistema, p. ej. Homebrew) con config propia y logs en la carpeta de datos; las descargas de Windows/Linux x64 traen KataGo v1.17.1 OpenCL oficial (SHA-256 fijado) + la red g170e b10c128 (`engines/katago`); «Instalar KataGo» en 1 clic donde falte. Pendiente: confirmar los términos de la red en katagotraining.org.
- [x] Gobanes, piedras y fondos (2026-10-02): arte propio renderizado (`tools/art/generate.py`): 6 gobanes, 5 juegos de piedras con variantes, 5 fondos en mosaico; los temas los usan y Preferencias → Tablero y piedras permite combinarlos libremente.
- [x] Web v2 (2026-10-02): tablero vivo en la portada (kaya y piedras de concha de Hoshi, apertura animada, el visitante puede jugar con capturas), funciones en pestañas (Estudiar · Joseki · Jugar · Sentir) sobre papel washi, sección «Tableros y piedras» sobre mesa de nogal, capturas regeneradas con el nuevo arte.
- [x] Chat de partida v2 (2026-10-02): burbujas (tuyas a la derecha), hora y canal (espectador, Malkovich), frases rápidas traducidas (`translated`: el rival las lee en su idioma), variantes y revisiones compartidas como notas, sin duplicados al reconectar, retirada por moderación, silenciar por partida con contador, sonido suave solo para mensajes nuevos (Preferencias), desplazamiento automático.
- [x] Ventanas y sonido (2026-10-03): barra de título propia en todas las ventanas (`HoshiTitleBar` en la plantilla de `Window`: icono, título, minimizar/maximizar/cerrar con los colores del tema; en macOS se conservan los semáforos nativos; donde el gestor de ventanas de Linux no deja extender, queda la barra del sistema). Botón de sonido en la barra inferior: silenciar todo (`AppSettings.Muted`, aplicado por `MutableSoundService` y deteniendo la música), volumen de efectos, sonido de piedras, música y su volumen. Preferencias → Tablero y piedras: miniaturas reales para todas las opciones (piedras vectoriales y fondos animados incluidos, `SkinThumbnails`) y «Del tema: …» dice qué usa el tema.
- [x] Donaciones (2026-10-04): PayPal (`paypal.me/CarlosFernandez934`) en `.github/FUNDING.yml` (botón «Sponsor» del repositorio, como enlace `custom`), sección y pie de la web, «Apoyar Hoshi ♥» en el menú ☰ y un agradecimiento discreto una sola vez tras cada actualización (`AppSettings.LastRunVersion`; nunca en la primera instalación, nunca ventanas emergentes). Todo es opcional: ninguna función depende de donar.
- [x] Instaladores con Velopack (2026-10-02): Setup.exe (Windows, por usuario, menú Inicio y escritorio), .pkg (macOS) y .AppImage (Linux) para x64 y ARM64, actualizaciones con deltas (≈ 0,3 MB frente a 48 MB en la prueba), portátiles `*-Portable.zip`; sustituye al actualizador propio. Probado de punta a punta en Linux (AppImage 0.1.1 → 0.1.2 con un clic). Pendiente: primera publicación real y probar Windows/macOS; firma de código.
- [x] Actualizaciones automáticas (2026-10-01): aviso de versión nueva (GitHub Releases), descarga verificada con SHA-256, instalación en sitio y reinicio; probado de punta a punta en Linux (0.1.1 → 0.1.2). Pendiente: probar en Windows y macOS reales.
- [x] Distribución (2026-10-01): `release.yml` (automático: corre cuando el CI de `main` pasa, versión 0.1.<n.º de ejecución>; no publica si desde la última versión solo cambió `docs/`, `.github/` o Markdown) publica ejecutables autocontenidos de un solo archivo (Windows x64/ARM64 .zip, macOS Hoshi.app arm64/x64 con firma ad hoc, Linux x64/ARM64 .tar.gz con `install.sh`) en GitHub Releases con nombres sin versión; la web detecta el sistema y enlaza `releases/latest/download/…`; `pages.yml` publica la web. Pendiente: firma de código (Authenticode / notarización de Apple).
- [x] Cierre (2026-10-01, 2.ª vez): el proceso seguía vivo tras cerrar. Causa probable: el `ConsoleLifetime` de Hosting bloquea `ProcessExit` hasta que el host termina de disponerse, y la disposición se hacía con `.Wait()` sobre el hilo de UI. Ahora: `DesktopLifetime` (no bloquea), disposición fuera del contexto de UI, `Environment.Exit` al final y vigilante que termina el proceso a los 8 s.
- [x] Sonidos (2026-10-01): la caché de sonidos se nombra por contenido; antes la piedra antigua quedaba en `cache/sounds` para siempre.
- [x] Localización inglés (por defecto) / español (2026-10-01): tabla `Hoshi.Core.Localization.Tr` (`Strings.*.cs` por capa), `{l:T Clave}` en XAML con cambio en vivo, selector en Preferencias → Apariencia.
- [x] Icono de la app (tablero de Go, `tools/gen_icon.py`) y página promocional estática bilingüe en `docs/site/`.
- [x] Botones «Análisis» y «Territorio» ocultos durante partidas de OGS en curso.
- [x] KataGo siempre de fondo desde que se abre la app, con píldora de carga; «Análisis» solo controla lo visual (2026-10-01).
- [x] Aviso cómico de atari (2026-09-30): temblor, gota de sudor y «uh-oh»; `Hoshi.Core.Atari`.
- [x] Efecto visual y sonido de capturas (2026-09-30): piedras que se rompen en cuñas en ola, chasquido + clacs + campanita.
- [x] Sonido suave de piedras (2026-09-30) y cierre limpio del proceso (apagado asíncrono con vigilante).
- [x] Análisis en vivo (2026-09-30): resultados parciales cada 0,25 s, prioridades (posición actual antes que la gráfica) y valoración instantánea con los candidatos de la posición anterior.
- [x] Progreso de arranque de KataGo (carga de la red, calibración OpenCL) en el panel y en «Probar KataGo»; motivo del cierre (código de salida y última línea de error) y rechazo de configuraciones GTP.
- [ ] Marcar en el árbol las jugadas malas; exportar la revisión al SGF (comentarios).
- [x] Motores GTP (2026-10-04, pedido por usuarios de Sabaki): cliente GTP v2 genérico (`Hoshi.Engines.Gtp`: protocolo sin ids, sincronización incremental con `play`/`undo`, `genmove`, `lz-analyze`/`kata-analyze` en streaming interrumpible). Preferencias → Motores: lista de motores (nombre, ejecutable, argumentos, comandos iniciales, Probar) + el KataGo de Hoshi automático (`hoshi_gtp.cfg`, 500 visitas). «Jugar contra un motor…» (Ctrl+G): tú o cualquier motor en cada color (también motor contra motor), tablero, hándicap, komi y reglas; el motor responde desde cualquier posición del árbol, Deshacer quita tu jugada y la suya, pausa/terminar en la barra lateral, abandono → `RE`. Consola GTP (tráfico de todos los motores, stderr opcional, comandos a mano con historial). El panel de análisis puede usar un motor GTP con `lz-analyze`/`kata-analyze`. Todo apagado durante partidas de OGS en curso del jugador. Probado con KataGo v1.17.1 (Eigen) real: partida 9×9 motor contra motor y análisis. Pendiente: reloj (`time_settings`/`time_left`) en partidas contra el motor; probar Leela Zero/GNU Go reales.

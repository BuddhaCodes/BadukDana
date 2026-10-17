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
- [x] Efectos con más criterio (2026-10-05, comentario de un usuario: explosiones excesivas en aperturas rutinarias y atari demasiado obvio): `MoveImportance` (calidad × criticidad según las candidatas de KataGo × pelea en el tablero) decide el tamaño de la celebración; las jugadas normales de apertura solo suenan como piedra. La música sube según la importancia, no pasa de las capas tranquilas en la apertura y se enfría antes. Halo discreto de grupos débiles (`GroupStrength`, con la propiedad de KataGo o por libertades) en lugar de las piedras que tiemblan y sudan. Sonido de piedra según el contexto: golpe suave para jugadas tranquilas, clac firme para contacto, hane, cortes y atari.
- [x] Efectos visuales regulables (2026-10-05): `AppSettings.Effects` = Apagados / Suaves / Completos para los impactos de las buenas jugadas, las capturas que se rompen y el aviso de atari. Suaves: destello pequeño y un anillo (sin temblor, grietas ni chispas), las piedras capturadas se desvanecen, y el atari solo con la gota de sudor. Se cambia a mitad de partida desde el botón ⚡ de la barra inferior, el panel de sonido, Preferencias → Análisis o la tecla F; bajar el nivel corta al instante un efecto en curso. Apagados también calla el «uh-oh» del atari.
- [x] Motores GTP (2026-10-04, pedido por usuarios de Sabaki): cliente GTP v2 genérico (`Hoshi.Engines.Gtp`: protocolo sin ids, sincronización incremental con `play`/`undo`, `genmove`, `lz-analyze`/`kata-analyze` en streaming interrumpible). Preferencias → Motores: lista de motores (nombre, ejecutable, argumentos, comandos iniciales, Probar) + el KataGo de Hoshi automático (`hoshi_gtp.cfg`, 500 visitas). «Jugar contra un motor…» (Ctrl+G): tú o cualquier motor en cada color (también motor contra motor), tablero, hándicap, komi y reglas; el motor responde desde cualquier posición del árbol, Deshacer quita tu jugada y la suya, pausa/terminar en la barra lateral, abandono → `RE`. Consola GTP (tráfico de todos los motores, stderr opcional, comandos a mano con historial). El panel de análisis puede usar un motor GTP con `lz-analyze`/`kata-analyze`. Todo apagado durante partidas de OGS en curso del jugador. Probado con KataGo v1.17.1 (Eigen) real: partida 9×9 motor contra motor y análisis. Pendiente: reloj (`time_settings`/`time_left`) en partidas contra el motor; probar Leela Zero/GNU Go reales.
- [x] Módulo de estudio (2026-10-10, tecla S o botón «Estudio», en cualquier partida): pins por jugada con categoría (error, buena jugada, pregunta, momento clave, idea, joseki, vida y muerte, tiempo, lección), autor, fecha e hilos de respuestas; dibujos sobre el tablero (flechas y zonas arrastrando, círculos, letras, líneas numeradas «¿y si…?» que no tocan la partida, borrar) en cinco colores; línea de tiempo de pins con filtro por categoría y pin anterior/siguiente; insignia violeta en el árbol. Todo vive en el SGF (propiedad `HS`, JSON): «Guardar estudio…» añade una copia legible por otros programas (comentario `— Hoshi study —` + `AR`/`SQ`/`CR`/`LB`) que Hoshi quita al abrir; «Abrir estudio…» suma notas, dibujos y respuestas de otra persona por jugada (o abre la partida si el tablero está vacío; pregunta si es otra partida). «¿Qué jugarías?»: test con la posición antes de cada jugada marcada (respuesta = la jugada para las buenas, la primera piedra de la línea «¿y si…?» para los errores; autoevaluación si no hay respuesta). «Informe…»: página HTML autónoma e imprimible con resumen por categorías, lecciones, cada momento en un mini tablero SVG con sus dibujos, las notas y el veredicto de KataGo (solo fuera de partidas de OGS en curso). En partidas de OGS en curso: notas y dibujos sí (los clics con herramientas de dibujo nunca llegan a OGS; los recibe el estudio), test y motor no; al reconectar se conservan las notas. Pendiente: probar con estudios reales compartidos entre dos personas.

## Fase 8 — Android (en curso, decisión 2026-10-11)
Alcance pedido: Android primero (iOS más adelante), con KataGo desde la primera versión, APK en GitHub y la web (sin Google Play por ahora), tocar y confirmar para colocar piedras, compilación en GitHub Actions.
- [x] Separar `Hoshi.App` (biblioteca) y `Hoshi.Desktop` (ejecutable «Hoshi»); recursos `avares://Hoshi.App`.
- [x] Vistas reutilizables (contenido de las ventanas y paneles de la ventana principal) con tests que buscan controles entre ámbitos de nombres.
- [x] Tocar dos veces para jugar (también en escritorio, desde Preferencias), en local y online.
- [x] Diseño de teléfono (`PhoneView`): barra inferior, hojas, páginas en lugar de ventanas, tarjeta para diálogos, botón atrás.
- [x] Ganchos de plataforma: KataGo incluido, AudioTrack, SoundPool, Keystore, archivos `content://`.
- [x] `Hoshi.Android` + `tools/android/build-katago.sh` + `.github/workflows/android.yml` (APK firmado si existen los secretos; adjunto a cada versión).
- [x] Primera compilación en GitHub Actions (2026-10-12).
- [ ] Prueba en un teléfono real (KataGo, sonido, OGS con OAuth en el navegador y vuelta a la app).
- [x] Enlace de descarga del APK en la web (tarjeta Android con captura; el botón principal ofrece el APK a los visitantes con Android) y en el README.
- [x] Efectos con IA más rápidos en el teléfono (2026-10-12): la gráfica espera mientras se juzga la jugada, 12 visitas bastan para el veredicto y, si KataGo tarda más de 0,8 s, el efecto sigue la pelea del tablero (el veredicto llega después para la música).
- [x] Calidad de vida (escritorio y teléfono): coordenadas opcionales, números de jugada (ninguno / últimas 10 / todas), ancho de la barra lateral recordado; en el teléfono, hoja inferior redimensionable (altura recordada), deslizar en la cabecera para recorrer la partida, vibración al colocar piedra y pantalla encendida durante la partida.
- [x] Estudio en el teléfono (2026-10-12): dibujos pendientes con Guardar/Descartar, flechas y áreas con dos toques o arrastrando (burbuja con el punto bajo el dedo), confirmación al borrar notas; 12 visitas de análisis por defecto en el teléfono; las casillas numéricas vacías explican qué esperan en vez del error de Int32.
- [ ] Pulido: diseño apaisado/tabletas, páginas de Preferencias y lobby pensadas para pantallas estrechas, entrenador de josekis en el teléfono, aviso de versión nueva.

## Fase 9 — Camino a la 1.0 (lista acordada 2026-10-12)
Para estudiantes y jugadores por igual. Numeradas para referirse a ellas; el orden dentro de cada bloque es la prioridad propuesta.

### Pedidos el 2026-10-13
- [x] Modo zen (Z), con variante silenciosa.
- [x] Pantalla de inicio (escritorio y Android) en lugar de la pantalla negra.
- [x] Flechas y áreas rápidas: el arrastre termina en la intersección más cercana.

### Imprescindible para la 1.0
- [x] 1. **Conteo en partidas locales y contra motores** (2026-10-13: tras dos pases; propuesta de KataGo si leyó la posición; tocar grupos; Aceptar escribe `RE`, Seguir jugando quita el último pase): marcar piedras muertas (sugeridas por KataGo o por el estimador propio), resultado con reglas japonesas o chinas y guardado en el SGF (`RE`).
- [x] 2. **Reloj local** (2026-10-13: menú Reloj con preajustes Fischer/byo-yomi/absoluto; corre para quien juega al final de la partida; rojo con 10 s; pierde por tiempo `B+T`/`W+T`) (absoluto, byo-yomi, Fischer) para partidas entre dos personas y contra motores; pierde por tiempo, pausa.
- [x] 3. **Automatch de OGS** (2026-10-13: pestaña Automatch con tamaño, ritmo y reglas; aviso de que es clasificatoria; pendiente probarlo en real) (hoy «llegará pronto» en el lobby).
- [x] 4. **Aviso de turno** (2026-10-13: tarjeta «Te toca contra…» con Abrir, sonido de chat y notificación de Android; sin notificación del sistema en escritorio) en otras partidas de OGS: sonido y notificación del sistema (escritorio y Android), sobre todo para correspondencia.
- [x] 5. **Abrir partidas terminadas de OGS** (2026-10-13: pestaña Terminadas del lobby, y número o enlace de partida) en el editor (descargar el SGF) para revisarlas con el estudio y KataGo.
- [x] 6. **Problemas (tsumego)** (2026-10-14: ventana/página Problemas, Ctrl+P; 8 problemas propios de Hoshi; importar colecciones SGF con RIGHT/WRONG; cajas de Leitner; cerrados durante tus partidas de OGS en curso): colecciones SGF con respuestas en variantes, acierto/fallo, siguiente problema, progreso y repetición espaciada (como el entrenador de josekis). Solo colecciones con licencia clara (ver §6 de CLAUDE.md).
- [x] 7. **Errores en el árbol de variantes** (2026-10-17: anillo rojo/ámbar en el árbol según la pérdida de KataGo; «Escribir la revisión en la partida» añade `BM`/`DO` y un comentario `[KataGo]` que se reemplaza al repetirla) (marca de color por calidad) y **exportar la revisión de KataGo al SGF** como comentarios.
- [x] 8. **Primera vez y tutorial** (2026-10-17: recorrido de 5 tarjetas al primer inicio, de nuevo desde el menú Ayuda; 4 lecciones de reglas en el tablero dentro de Problemas): recorrido corto por la interfaz y, para principiantes, las reglas básicas con ejercicios en el tablero.
- [ ] 9. *(pendiente del usuario: necesita un teléfono real y la clave de firma)* **Prueba real en Android** (KataGo, sonido, OAuth de OGS volviendo a la app) y firma del APK configurada para que las actualizaciones se instalen encima.
- [ ] 10. *(pendiente del usuario: necesita grabar tráfico real con su cuenta de beta)* **Fixtures reales de beta.online-go.com** para cerrar las diferencias del protocolo (Fase 5).

### Muy deseables
- [x] 11. *(2026-10-17: Archivo → Compartir: PNG de 800 px con el tema actual; GIF con codificador propio, paleta por corte de mediana y solo el rectángulo que cambia)* **Exportar el tablero** como imagen (PNG) y una secuencia como GIF animado.
- [x] 12. *(2026-10-17: jugador, fechas y resultado a la vez; patrón del área de estudio o de la esquina de la última jugada, en las 8 simetrías y con colores cambiados, buscado en segundo plano; «Revisar» abre en la jugada del patrón)* **Biblioteca de partidas con búsqueda**: por jugador, fecha y resultado, y por patrón en una zona del tablero (al estilo Kombilo).
- [x] 13. *(2026-10-17: al escribir la revisión de KataGo, los errores de 3 puntos o más con mejor jugada pasan a Problemas → De mis partidas, guardados en `tsumego/from-my-games.sgf`)* **Problemas desde tus partidas**: los momentos donde KataGo vio un error grande se convierten en «encuentra la mejor jugada».
- [x] 14. *(2026-10-17: solo soporte; el modelo humano de KataGo se descarga aparte y se elige en Preferencias → Análisis; «KataGo (estilo humano)» con niveles 20k–5d en «Jugar contra un motor»; sin probar con el modelo real)* **KataGo con nivel humano** (p. ej. 15k–1d, modelo «human-like») para practicar contra un rival a tu medida.
- [x] 15. *(2026-10-17: desde 600 px y más ancho que alto, la hoja va a la derecha del tablero; los teléfonos avisan de versiones nuevas y abren la web del APK; pendiente probarlo en un teléfono/tablet real)* **Tablet y horizontal en Android**, y aviso de versión nueva en el teléfono.
- [x] 16. *(2026-10-17: tema Alto contraste; tamaño de la interfaz 100–150 %; casilla para jugar escribiendo D4/pasa; el tablero y el estado se anuncian al lector de pantalla — en Android depende del soporte de Avalonia)* **Accesibilidad**: jugadas y estado legibles por lector de pantalla, alto contraste, tamaño de letra.

### Compartir y redes sociales
Todo lo que sale del equipo es una acción explícita del usuario; los nombres de los jugadores se pueden ocultar al compartir y la marca de Hoshi es opcional.
- [x] 17. *(2026-10-17: tarjeta cuadrada 1080², 16:9 1600×900 y vertical 1080×1920; opciones «Ocultar los nombres» y «Mostrar Hecho con Hoshi», recordadas)* **Tarjeta para compartir un momento**: imagen del tablero con los jugadores, la jugada, la nota del estudio y el veredicto de KataGo, en formatos cuadrado (Instagram), 16:9 (X/Twitter, Discord) y vertical (historias).
- [x] 18. *(2026-10-17: GIF de la línea de la jugada mostrada hasta su final; las capturas y los errores graves de KataGo se quedan más tiempo, con círculo; vídeo MP4 no)* **Vídeo o GIF de la partida completa** («timelapse», con los efectos de las jugadas importantes) o de una línea «¿y si…?», listo para redes.
- [x] 19. *(2026-10-17: Android con `FileProvider` sobre la caché `share/` e `ACTION_SEND`; escritorio con portapapeles de texto y guardar archivo; pendiente probarlo en un teléfono real)* **Menú «Compartir» del sistema** en Android (y portapapeles en escritorio) para imágenes, SGF y estudios.
- [x] 20. *(2026-10-17: `TextDiagram` en Hoshi.Sgf: `$$` con marcas y coordenadas, y bloque ``` con la última jugada entre corchetes)* **Copiar como diagrama de texto**: formato de Sensei's Library (`$$`) para foros y Reddit, y bloque de código para Discord.
- [x] 21. *(2026-10-17: «Esta jugada como problema»: la posición anterior, «Juegan…», y la respuesta copiada como `||D16||` y `>!D16!<`)* **Problema para compartir**: imagen «negras juegan» con la respuesta oculta (spoiler de Discord/Reddit) sacada de tu estudio o de tus errores.
- [x] 22. *(2026-10-17: posiciones calculadas por Hoshi y guardadas en la página; script propio, SVG, flechas, variantes y notas; sin archivos externos)* **Visor web independiente**: exportar la partida con su estudio a un único `.html` que se abre en cualquier navegador (para un blog, la web del club o un mensaje).
- [x] 23. *(2026-10-17: Ctrl+Mayús+C / Ctrl+Mayús+V; pegar un enlace o número de OGS descarga la partida)* **Copiar/pegar SGF y enlaces de OGS**: pegar un SGF o un enlace de partida de OGS lo abre; copiar la partida actual como SGF.
- [x] 24. *(2026-10-17: Compartir → Resumen; jugador = autor del estudio o el nombre de la mayoría de partidas guardadas; precisión de las revisiones de KataGo guardadas en `reviews.json`, solo recuentos)* **Resumen del mes / del año**: tarjeta con partidas jugadas, mejores jugadas, precisión media según KataGo y josekis aprendidos (calculado en el equipo, nada se envía).
- [x] 25. *(2026-10-17: código listo, apagado por defecto; necesita que el usuario cree una aplicación en discord.com/developers y pegue su id en Preferencias; IPC local `discord-ipc-N`, solo el tipo de actividad; sin probar con Discord real)* **Estado en Discord** (opcional, apagado por defecto): «Estudiando una partida» / «Jugando en OGS», sin datos del rival.

### Ajustes tras la 1.0 (2026-10-17)
- [x] Botón de salir del zen visible (escritorio: píldora con Z; teléfono: botón grande abajo).
- [x] Barra inferior adaptable: solo iconos bajo 1000 px; territorio, josekis y en línea pasan a «⋯» bajo 720 px.
- [x] Reloj solo en partidas locales; «Pausar la partida» (Ctrl+Espacio) para reloj y motor, nunca en línea.
- [x] Entrenador de josekis: «Volver a la partida» visible y «Atrás una jugada» (← o Retroceso).
- [x] Textos claros para el test, el conteo, «hacer línea principal» y «borrar» (que ahora pregunta).
- [x] Android: estudio en tres partes (esta jugada, dibujar, partida) y menú agrupado.
- [x] Creador de replays (escritorio): MP4 con FFmpeg, GIF y página web animada.
  - [x] Efectos en las jugadas críticas (2026-10-17): la mejor o excelente donde había ≥ 3 puntos en juego es «Jugada clave» (≥ 8 y la mejor: «Jugada brillante») con el impacto del tablero (destello, ondas, chispas, grietas, temblor), luz dorada y su explosión; los errores y errores graves, onda roja, sacudida y un golpe sordo. Nada en la apertura. Vídeo a 30 fps, GIF a 12,5; la web, con onda y sacudida en CSS.
- [ ] Conectar KGS e IGS: investigación en `docs/SERVERS.md`; esperan permiso de los servidores y la decisión sobre las contraseñas. Fox, descartado para jugar.
- [x] Rediseño (lienzo «Hoshi: rediseño en lienzo», 2026-10-17): «Estrella polar» como base y «Kaya» (claro) como tema.
  - [x] Tema por defecto con la paleta noche/oro/rojo sello y títulos en Shippori Mincho; tema Kaya claro (Fluent pasa a claro).
  - [x] Escritorio: inspector de revisión (tres candidatas de KataGo, tres errores grandes, «Siguiente error» o E, puntos rojos en la gráfica) y paleta de herramientas de estudio flotando sobre el tablero.
  - [x] Android: pantalla de inicio (continuar, partida nueva, seis modos), «Inicio» en el menú y con Atrás; jugadores como dos tarjetas.
  - [x] Web: paleta y títulos nuevos, Android en la portada y la sección «Una partida, tres capítulos» (序 中 終).
  - [ ] Pendiente del rediseño: encabezado con pestañas Jugar/Estudiar/Revisar en escritorio; lista de jugadas con gráfica a la izquierda; en Android, tarjetas de jugadores encima y debajo del tablero en partidas OGS y «Confirmar jugada» con la coordenada; en la web, tarjetas de plataforma con la detectada arriba y la variante Kaya clara; pasar los frames a Figma.

### Después de la 1.0
Diseños y recomendaciones en `docs/AFTER_1_0.md` (2026-10-17); cada una espera una decisión del usuario.
- [ ] 26. Sincronizar estudios y progreso entre escritorio y teléfono.
- [ ] 27. Más servidores (KGS, Fox) si sus términos lo permiten.
- [ ] 28. iOS.
- [ ] 29. Modo profesor: tablero compartido en vivo con alumnos.

# Guía visual

Objetivo: **el tablero es el protagonista** y la interfaz se aparta, con identidad propia. Hoshi nació inspirado en
Sabaki, pero desde el rediseño de 2026-10-17 tiene su propia estructura (cabecera, columnas y pie, ver «Layout») y
ningún recurso de Sabaki. Hoshi (星, «estrella») tiene seis temas, elegibles en *Preferencias* (☰ → Preferencias…,
Ctrl+,), que cambian fondo, paleta, fuentes, iconos, piedras y animaciones al momento.

## Temas (`Themes/HoshiTheme.cs`, aplicados por `ThemeService`)

| Tema | Fondo (propio, animado salvo indicación) | Acento | Fuentes (título · UI) | Iconos | Piedras | Efecto al jugar |
|---|---|---|---|---|---|---|
| **Mochi** (claro, por defecto desde 2026-10-18; el estilo de la web) | Crema `#F8F1E7` con nubes pastel (rosa, menta, lavanda, mantequilla) que derivan muy despacio; tinta ciruela `#2D2440`, paneles blancos y rosados, barra de edición ciruela; esquinas redondas | rosa `#C8577A` | Fredoka · Manrope | Lucide | Pizarra y concha (shin-kaya) | Asentarse |
| **Estrella polar** (antes «Cielo nocturno», paleta del rediseño 2026-10; por defecto hasta 2026-10-17) | Azul noche `#0B1026` con ~240 estrellas que titilan, nebulosa que deriva, constelaciones tenues y una estrella fugaz cada ~23 s | oro `#E8C36A` | Shippori Mincho · Manrope | Phosphor light | Perla | Halo dorado |
| **Kaya** (claro) | Papel washi (estático) bajo un goban de shin-kaya claro; cromo crema `#F1E6CF`, tinta `#1E1A14`, barra de edición índigo `#26357F` | rojo sello `#B23A2A` | Shippori Mincho · Manrope | Phosphor regular | Pizarra y concha | Onda de tinta |
| **Tinta y oro** | Niebla de tinta sumi-e que respira y un ensō de pincel en oro tenue enmarcando el tablero | oro `#C9A45C`, rojo sello `#B23A2E` | Shippori Mincho · Manrope | Phosphor regular | Pizarra y concha | Onda de tinta |
| **Jardín zen** | Arena rastrillada con ondas alrededor de dos rocas, moviéndose muy despacio | musgo `#9DBB7F` | Zen Kaku Gothic New | Lucide | Pizarra y concha | Onda en la arena |
| **Minimal cálido** | Papel washi con fibras (estático) | terracota `#D07A4A` | Manrope | Tabler | Suaves | Asentarse |

- Relojes siempre en JetBrains Mono. Las fuentes van recortadas a latín (+ 星 碁).
- Un tema claro (`IsLight`) cambia también Fluent a su variante clara (`RequestedThemeVariant`).
- Esquinas: `Radius.Control` / `Radius.Soft` / `Radius.Card` / `Radius.Palette` (3 / 6 / 10 / 14 px; en un tema `Rounded`, como Mochi, 10 / 12 / 18 / 22) y los `ControlCornerRadius` / `OverlayCornerRadius` de Fluent (4 / 8 → 10 / 14).
- Mochi es amable sin ser infantil: Fredoka solo en títulos y marca, el texto sigue en Manrope; el tablero y las piedras se quedan sobrios. Kuro y Shiro (las dos piedras con cara de la web) solo aparecen en el arranque.
- Recursos DynamicResource que cambia el tema: `Bg.*`, `Text.*`, `Accent`, `Danger`, `Good`, `Border.Subtle`, `Bg.EditBar`,
  `EditBar.Foreground/Hover`, `Bg.Surround`, `Font.UI/Title/Mono`, `Font.Size.Title`, `Radius.*`, `Icon.*` (40 iconos; borrar todos los dibujos usa `Icon.Trash`, en el color de peligro),
  `Theme.Board` (`BoardStyle`), `Theme.Background`, `Theme.Animations`; también el acento de Fluent.
- «Animaciones» (Preferencias) apaga el fondo animado y las animaciones de piedra; se guarda en `settings.json`.

## Animaciones

- Piedra nueva: aparece 14 % más grande y elevada (sombra mayor) y se asienta en 200 ms (ease-out cúbico); el efecto
  del tema dura 650 ms. Solo cuando aparece exactamente una piedra en la última jugada (jugar o avanzar 1), no al saltar.
- Paneles que aparecen (panel en línea, chat, barra de edición): fundido de 280 ms.
- Botones de la barra: fondo con transición de 150 ms y leve pulsación (escala 0.94). Barra de edición: transición de color.
- Tarjetas de Preferencias: se elevan 3 px al pasar el ratón; borde de acento en la seleccionada.
- Fondo animado a ~24 fps con movimiento muy lento; se detiene fuera de pantalla o con animaciones apagadas.

## Origen

Hoshi tomó de Sabaki (MIT) la idea de una interfaz silenciosa con el tablero en el centro. Desde 2026-10-17 no
incluye ninguno de sus recursos (se quitaron el tema Clásico, el tatami y la textura `board.png` de Shudan); las
piedras «degradado clásico» conservan los colores de los SVG de Shudan (aviso en `THIRD_PARTY_NOTICES.md`).

## Principios

1. El tablero ocupa todo el espacio disponible sobre el fondo del tema; la UI se adapta a él.
2. Cabecera arriba (quién juega, pestañas, herramientas), pie bajo el tablero (moverse por la partida y acciones).
3. Tipografía pequeña y discreta; iconos lineales monocromos.
4. Toda acción importante tiene atajo de teclado (ver abajo).
5. Modo "zen" (F11): solo tablero *(pendiente)*.

## Paleta por defecto (`Styles/Tokens.axaml`, la de Estrella polar)

`Bg.Window` `#0B1026` · `Bg.Bar`/`Bg.Sidebar` `#0F1530` · `Bg.Panel`/`Bg.Properties` `#141A3A` · `Bg.PanelAlt` `#1B2350` ·
`Border.Subtle` `#2A315A` · `Text.Primary` `#E9E6DA` · `Text.Secondary` `#9AA0C3` · `Accent` `#E8C36A` · `Danger` ·
`Good` · `Bg.EditBar` `#2E3F73`. El tema elegido los reemplaza al arrancar (`ThemeService`).

## Tablero (`GoBoardControl`, `BoardTextures`)

- La madera del tema (`BoardStyle.Texture`, de `Assets/Art/Boards`) o la kaya procedural teñida con `Wood`, cubriendo el tablero.
- Borde y sombra del tema; brillo vertical sutil. Líneas, hoshi y coordenadas del tema; bordes exteriores 1.5×.
- Márgenes: 0.8 celdas sin coordenadas, 1.35 con coordenadas.

## Piedras

- Radio 0.46 celda. «Degradado clásico» (vectorial, colores de los `stone_±1.svg` de Shudan):
  - Negras: degradado vertical `#443432` → `#0B0B0B`, brillo gris en la mitad superior, borde negro.
  - Blancas: `#FFFFFF` → `#C9D1FF`, reflejo claro en el borde inferior, borde `#C3C3C3`.
- Sombra: disco `rgba(23,10,2,.4)` + sombra `0 .1 .2` celdas del mismo color.
- *Fuzzy placement*: desplazamiento estable por intersección (0.03 celda en 1 de 8 direcciones, o ninguno).
- Piedra fantasma al 40 %; no aparece si la jugada es ilegal.

## Gobanes, piedras y fondos propios (`Themes/Skins.cs`, `tools/art/generate.py`) — 2026-10-02

Arte de Hoshi renderizado por procedimiento (numpy + Pillow, semillas fijas; sin imágenes de terceros), en `Assets/Art/`:

- **Gobanes** (1024 px JPEG, veta de arriba abajo): kaya masame (veta recta), kaya itame (arcos), shin-kaya, katsura, bambú laminado, nogal oscuro (líneas color crema). También la kaya procedural teñida por el tema.
- **Piedras** (192 px PNG con alfa, sombreado de lente biconvexa vista desde arriba, supermuestreo 3×): concha y pizarra (8 blancas con bandas de crecimiento en ángulos distintos, 4 negras), yunzi, cristal, cerámica mate, jade y obsidiana; más las vectoriales (perla, suaves, degradado clásico). La variante es estable por intersección (`Skins.StoneSprite`), así que una piedra nunca cambia de aspecto.
- **Fondos** (1024 px JPEG sin costuras, dibujados a la mitad para nitidez en HiDPI, con viñeta): sashiko índigo (seigaiha), mesa de nogal, pizarra, lino carbón, persiana de bambú; además los animados de los temas y washi.
- Cada tema trae su combinación; Preferencias → **Tablero y piedras** permite mezclar goban, piedras y fondo con cualquier tema (`AppSettings.BoardSkin/StoneSkin/BackgroundSkin`, null = el del tema; `Skins.Compose` sustituye solo esa parte del `BoardStyle`).
- Regenerar: `python3 tools/art/generate.py --sheet hoja.png`.

## Barra de título y sonido (2026-10-03)

- Todas las ventanas usan la plantilla de `Window` de `Styles/Base.axaml`: `HoshiTitleBar` (36 px, `Bg.Bar`) arriba y el contenido debajo. **Desde 2026-10-18** la ventana principal no tiene barra aparte: su cabecera es la barra de título (`HoshiTitleBar.IsMerged` en la ventana, `HoshiTitleBar.IsDragArea` en la cabecera: arrastrar mueve, doble clic maximiza) y los botones minimizar/maximizar/cerrar (`CaptionButtons`, 46×55 px) van en su extremo derecho; en macOS la cabecera deja 78 px a los semáforos. Si la cabecera se oculta (zen, josekis) vuelve la barra fina. Las ventanas sin decoraciones (el arranque) no reciben barra ni extienden el área cliente. Botones de 46 px; pasar el ratón usa `Bg.PanelAlt` y cerrar se pone rojo (#C42B1C). Doble clic maximiza; arrastrar mueve. Los diálogos con dueño no muestran minimizar; las ventanas sin `CanResize` no muestran maximizar.
- El botón de sonido (icono `Volume`/`Muted` de cada juego de iconos) abre un panel con «Silenciar todo», efectos, piedras, música y volumen. Los flyouts usan `Bg.Panel` y esquinas de 10 px.

## Marcadores

- Última jugada: círculo hueco del color contrario (no se dibuja si hay marca SGF en ese punto).
- `TR`, `SQ`, `CR`, `MA`, `LB` (etiqueta con fondo del tablero en intersección vacía).
- Conteo en línea: cruces sobre las piedras muertas.

## Layout de MainWindow (rediseño 2026-10-17, como el lienzo)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ 星 Título · detalles   Negras ● ○ Blancas (relojes)   [Jugar|Estudiar|Revisar] (Crear replay) ⊞ ☆ ⊕ ϟ ♪ ☰ │
├───────────────┬──────────────────────────────────────────┬───────────────────┤
│ (Revisar,     │                                          │ análisis /        │
│ ≥ 1180 px)    │               TABLERO                    │ inspector         │
│ gráfica       │ (paleta de estudio flotando a la         │ árbol             │
│ jugadas       │  izquierda en Estudiar)                  │ comentario,       │
│ coloreadas    │                                          │ chat o estudio    │
├───────────────┴──────────────────────────────────────────┤                   │
│ ⏮ ◀ ▶ ⏭  (Siguiente error)  ───────●────── 73 / 187  Pasar Deshacer │                   │
└──────────────────────────────────────────────────────────┴───────────────────┘
```

- Cabecera (`AppHeader`, 56 px): título y detalles de la partida (se ocultan por debajo de 1280 px), jugadores con
  turno, relojes y capturas, las pestañas, «Crear replay» en Revisar y las herramientas solo con icono; por debajo
  de 1000 px territorio, pistas de joseki y en línea pasan a «⋯». En modo edición muestra las herramientas de edición
  con `Bg.EditBar`.
- Pie (`BottomBar`, 52 px): primera/anterior/siguiente/última, «Siguiente error» en Revisar, barra de posición sobre
  toda la línea (`MoveListViewModel.Position`) con «n / total», Pasar y Deshacer.
- Barra lateral (220–420 px, redimensionable): partida contra el motor, panel en línea, análisis e inspector, árbol,
  estado y comentario, chat o estudio.
- Zen oculta cabecera, pie y barra lateral; el entrenador de josekis oculta cabecera y pie.

## Árbol de variantes (`GameTreeControl`, como el *game graph* de Sabaki)

- Rejilla de 22 px, nodos de radio 5 con contorno `#111`.
- Relleno `#EEE`; comentario/nombre naranja `rgb(255,174,61)`; `BM` rojo, `DO` morado, `IT` azul, `TE` verde.
- Pases como cuadrados, nodos sin jugada (raíz, setup) como rombos.
- «Pista actual» (raíz → nodo actual → primeros hijos) a pleno color con aristas `#CCC` de 2 px; el resto al 50 % con
  aristas `#777` de 1 px; variantes unidas en diagonal. Nodo actual con contorno `#EEE` de 2 px.

## Territorio y análisis

- **Territorio (T):** cuadraditos del color del dueño probable; su tamaño y opacidad crecen con la seguridad. Territorio seguro y piedras muertas: cuadrado grande opaco. Resumen en la barra lateral: seguro (+potencial) de cada color y ventaja estimada, indicando si viene de la estimación rápida o de KataGo.
- **Análisis (A):** panel en la barra lateral con winrate (barra negro/blanco), ventaja, valoración de la última jugada en color (verde = mejor/excelente, verde oliva = buena, ámbar = imprecisa, rojo = error), la mejor alternativa y la gráfica de ventaja por jugada (arriba = negras; clic para ir). Sobre el tablero, hasta 3 sugerencias como discos azules con el % de victoria de quien juega y el cambio de puntos; la mejor con borde blanco.
- Botones de la barra con subrayado de acento cuando están activos.
- **Celebración de jugadas que importan** (solo con análisis activo, al jugar; no al navegar). Llega cuando KataGo termina de valorar la jugada. Su fuerza no depende solo de la calidad: `MoveImportance` la limita según el momento (2026-10-05, a petición de un usuario): criticidad (puntos entre la mejor jugada y la media de las 4 siguientes candidatas de KataGo) + intensidad de la pelea alrededor (`FightMeter.Intensity`: contacto, libertades cortas, capturas); ≥ 8 → 3, ≥ 4,5 → 2, ≥ 2,5 → 1, y nunca más que la calidad. En la apertura (≈ 1/18 del tablero: 20 jugadas en 19×19) una jugada tranquila sin pelea ni criticidad ≥ 4 no tiene celebración: solo suena la piedra. Sin motor (OGS en curso), la apertura resta 1 a la fuerza de la pelea.
  - *Buena* (fuerza 1): golpe seco (`impact_small`) y una onda pequeña.
  - *Excelente* (2): explosión (`explosion_medium`), destello, onda expansiva, chispas, temblor suave y un cráter pequeño con 5 fracturas.
  - *La mejor* (3): explosión cinemática (`explosion_big`: caída de subgraves, «braam» grave, cola de escombros y reverberación), destello grande, dos ondas, ~56 chispas con estela, temblor fuerte y **suelo dañado**: cráter irregular con fondo astillado y labio levantado, 8 fracturas radiales en facetas (anchas en el cráter, afinándose hasta un hilo, con bifurcaciones), fracturas en anillo que las unen y astillas de madera despedidas. Relieve con luz desde arriba a la izquierda (halo oscuro de oclusión y labio claro abajo a la derecha). Las fracturas nacen incandescentes (metal fundido) y se enfrían a grietas oscuras; todo se desvanece en ~3,2 s.
  - Las grietas se dibujan bajo las piedras y recortadas al tablero; el resto encima. Todo determinista a partir de una semilla (`ImpactEffect`). Sin animaciones (Apariencia) solo suena.
  - Intensidad (`EffectsLevel`, `GoBoardControl.Effects`): **Completos** (todo lo anterior), **Suaves** (fuerza 1 sea cual sea la jugada: destello pequeño y un anillo fino; capturas que se encogen y desvanecen sin esquirlas; halo de grupos débiles quieto) y **Apagados**. Botón ⚡ (`Icon.Effects`, atenuado cuando están apagados) en la barra inferior, sección en el panel de sonido, Preferencias → Análisis y tecla F (Completos → Suaves → Apagados).

## IA siempre de fondo

- Si KataGo está configurado, arranca al abrir Hoshi y analiza en todo momento (no en partidas de OGS en curso), para que las valoraciones, los efectos y la música funcionen aunque el análisis no se muestre.
- Mientras KataGo carga o calibra la gráfica, una píldora discreta abajo del tablero («Despertando a KataGo…», «Cargando la red neuronal…», «Calibrando la tarjeta gráfica (paso n/m)…») con barra indeterminada; se desvanece al primer resultado.
- El botón «Análisis» (A) solo muestra u oculta el panel, la gráfica y las sugerencias sobre el tablero. La gráfica de la partida solo se rellena mientras se muestra.

## Música adaptativa

- Lo-fi tranquilo (Dm9 – G13 – Cmaj9 – Am9, 80 BPM, vinilo y cinta). Capas: 0 piano eléctrico (siempre), 1 bajo y bombo, 2 batería con swing, 3 arpegio pulsado con eco, 4 «hype» (acordes con bombeo, hi-hats a semicorcheas, palmas, melodía brillante), 5 «overdrive» (bombo a negras, bajo rodante a semicorcheas, arpegio supersaw, redobles y platos). Cada capa nueva entra con un golpe («level up»: barrido inverso, boom y plato); con el calor alto las capas melódicas bombean con el bombo (sidechain).
- Calor (0–5,3), más calmado desde 2026-10-05: buena +0,5, excelente +0,75, la mejor +1,0, más +0,12 por jugada de racha (≤ 25 s entre buenas, máx. +0,5), todo multiplicado por la importancia del momento (`MoveImportance`: 0 → ×0,3, 1 → ×0,55, 2 → ×0,8, 3 → ×1). En la apertura no pasa de 1,0 (solo piano y bajo). Imprecisa −1, error −2, error grave → 0 con tape-stop. Tras 10 s sin buenas jugadas se enfría 0,08/s.
- Cada capa entra 0,85 de calor después de la anterior; el filtro se abre de 900 Hz (apagado, acogedor) a ~18 kHz.

## Sonido de piedras

- Según el contexto (2026-10-05): las jugadas tranquilas (apertura, extensiones, conexiones) suenan con el golpe suave al 70 %; las jugadas con intención (contacto con piedras rivales, hane, cortes, atari: `FightMeter.Intensity` ≥ 0,6) con `stone_firm` al 90 %: la misma piedra puesta con firmeza, un clac más seco y brillante (centroide ~1,6 kHz frente a ~0,6 kHz), sin resonancia; tres variantes. Sustituible con `stone_firm.wav`/`.mp3`.
- Cada piedra nueva (jugada, recibida de OGS o al avanzar una jugada) suena con un golpe suave y apagado, como dejar un móvil sobre la mesa (v2, 2026-10-01): el canto toca primero y la piedra se asienta plana 12–20 ms después, con un mínimo rebote; sin brillo ni campanita, para no cansar tras cientos de jugadas. Tres variantes que rotan. En Windows va por `PlaySound` desde memoria, con poca latencia. Se puede sustituir con `stone.wav`/`.mp3` en la carpeta `sounds/`. Se desactiva en Preferencias → Análisis.

## Capturas

- **Visual** (`CaptureEffect`, con animaciones activadas): cada piedra capturada destella al romperse y se parte en 5–6 cuñas con su propia textura que salen girando, alejándose de la piedra que captura, encogen y se desvanecen (0,6 s), con una nube de polvo. Las piedras caen en ola desde la captura (45 ms por casilla, máx. 0,4 s).
- **Sonido**: `capture_small` (1–2 piedras: chasquido, escombros, dos clacs al recogerlas y campanita de dos notas) y `capture_big` (3 o más: más clacs, arpegio re–fa–la y un golpe grave). Suena junto con el «pachi» de la piedra; mismo interruptor que el sonido de piedras.

## Grupos débiles

- Sustituye al aviso cómico de atari (temblor y gota de sudor) desde 2026-10-05, a petición de un usuario que esperaba ver la fuerza de los grupos. `Hoshi.Core.GroupStrength` lee cada grupo: con el mapa de propiedad de KataGo (de la posición mostrada), la propiedad media de sus piedras para su dueño: > 0,6 fuerte (no se marca), > 0,2 inestable, > −0,2 débil, si no en peligro serio; «inestable» solo si hay una piedra rival a ≤ 2 casillas (las piedras sueltas de la apertura no se marcan); un atari es como poco débil. Sin KataGo: atari = peligro serio, 2 libertades en contacto = débil.
- Dibujo: halo radial bajo las piedras (1,6 × radio): azul `#4682DC` al 32 % (inestable) o 58 % (débil); rojo apagado `#CD483C` para el peligro serio, que respira despacio (2,6 s) con efectos completos y queda quieto con suaves. Con efectos apagados no se dibuja.
- Al entrar en atari (una vez por grupo) sigue sonando un «uh-oh» suave. Desactivado en partidas de OGS en curso, como el análisis. Interruptor «Grupos débiles» en Preferencias → Análisis.

## Estudio

- Panel en la barra lateral en lugar del comentario (o del chat online), 500 px con desplazamiento: herramientas (Jugar · Flecha · Zona · Círculo · Letra · ¿Y si…? · Borrar) con iconos, cinco colores (dorado `#D6961E`, rojo `#D64034`, azul `#2E6ED6`, verde `#289650`, violeta `#8C50C8`), una línea de ayuda, la línea de tiempo (puntos del color de la categoría, apilados si una jugada tiene varios; marca de la jugada actual en acento), las tarjetas de pins (borde izquierdo del color de la categoría, autor y hora, respuestas con línea fina, caja «Responder…» con Intro), fichas de categoría para el pin nuevo y las acciones (Guardar estudio, Abrir estudio, Informe, ¿Qué jugarías?).
- Colores de categoría: error `#D6403A`, buena jugada `#2E9E54`, pregunta `#3B82D6`, momento clave `#D9A12A`, idea `#8C50C8`, joseki `#1F9E9A`, vida y muerte `#E0702A`, tiempo `#8A8F9E`, lección `#C2577F`.
- Tablero: zonas translúcidas con borde discontinuo bajo las piedras; flechas con halo blanco; círculos con halo; letras en disco de color; las líneas «¿y si…?» como piedras translúcidas numeradas con un aro del color. En el test, píldora arriba con la pregunta o el resultado; verde = la respuesta, rojo = tu intento, «!» = la jugada de la partida.
- Informe: papel claro, serif para títulos, un mini tablero SVG por momento; se imprime bien.

## Teléfono (Android)

- Vertical: arriba los jugadores y el turno (online: el panel con relojes y acciones); el tablero ocupa el ancho, sin coordenadas por defecto (se activan en Preferencias); debajo, una hoja a la vez (330 px por defecto; se ajusta arrastrando el asa de arriba y se recuerda; arrastrada casi hasta abajo se cierra: jugadas y comentario, análisis, estudio, chat o menú) y la barra inferior de 48 px con iconos y etiquetas pequeñas. **Desde 2026-10-18** la barra es más corta: Atrás · Adelante · Pasar · Deshacer · Revisar · Más (online: Atrás · Adelante · Revisar · Chat · Más; pasar, pedir deshacer, abandonar y salir están en la fila de acciones bajo tu tarjeta). «Revisar» abre una sola hoja con un selector Jugadas | Análisis | Estudio (análisis oculto durante tus partidas de OGS) y recuerda la última parte usada. «Más» es una cuadrícula de mosaicos (icono + etiqueta, tres por fila) por grupos: partida nueva, jugar, esta partida, reloj, compartir (plegado) y ajustes; nada se quitó. En partidas locales la jugada y el estado van en el subtítulo de la cabecera (sin fila aparte: más sitio para el tablero). El botón activo se pinta con el acento; los desactivados al 35 %.
- Tocar y confirmar: el primer toque deja la piedra al 72 % con un aro de acento y líneas guía en su fila y columna; el segundo toque en el mismo punto la juega.
- Estudio con tocar y confirmar: los dibujos (flechas, áreas, marcas, letras, líneas «¿y si…?», borrados) quedan al 70 % de opacidad y, arriba del panel de estudio, una franja con borde de acento dice «Aún sin guardar» con «Descartar · Guardar» (sin tapar el tablero); cambiar de jugada o cerrar el estudio los descarta. Borrar una nota o todos los dibujos pide confirmación. Flechas y áreas: arrastrando (una burbuja sobre el dedo nombra el punto que tapa, p. ej. «Q16», y un aro lo marca) o con dos toques (inicio y final; el inicio queda marcado). Trazos con grosor mínimo de 3 px para tableros pequeños.
- Deslizar el dedo a los lados sobre la cabecera recorre la partida (derecha: adelante; una jugada cada 28 px). Cada piedra da un toque de vibración (se apaga en Preferencias) y la pantalla no se apaga mientras hay una partida en curso.
- Preferencias (escritorio y teléfono, 2026-10-18): la pestaña Apariencia se desplaza entera, con «Tema global» primero y «Opciones» debajo; cada tarjeta usa la tinta de su tema. En la pestaña de tablero, «Usar los del tema global (Mochi)» nombra el tema al que vuelve. El tema guardado es `null` por defecto; un «night» que nunca se eligió (lo escribían todos los guardados) pasa a Mochi una vez (`ThemeChoice`).
- Teléfono, 2026-10-18 (segunda pasada): el territorio se lee como zonas (un lavado del color del dueño sobre cada punto, fuerte en el seguro y suave en el probable, bajo las piedras; cuadros de 0,48 celdas) y, mientras está encendido, una píldora sobre el tablero dice quién va delante con un ✕ para apagarlo. Zen en el teléfono oculta también la cabecera de jugadores, tu tarjeta online y las barras del sistema (vuelven deslizando desde el borde); «Confirmar L7» sigue apareciendo. El fondo del tema se mueve también en el teléfono (12 fps, quieto en zen) y las barras del sistema toman el color de la barra del tema (iconos oscuros en temas claros).
- Partida contra un motor: el reloj se elige en el diálogo de la partida («Sin reloj» o un preset; se recuerda) y empieza con ella; durante esa partida el menú solo deja pausar o quitar el reloj.
- Familia Mochi (redondeada, Fredoka, Lucide, nubes pastel): Mochi, Matcha (verde té, katsura), Yuzu (mantequilla y cítrico, kaya itame) y Kuro mochi (oscuro: ciruela profundo, rosa suave, piedras de cristal).
- Preferencias en el teléfono: márgenes de 14 px, pestañas a 17 px, la pestaña Apariencia se desplaza entera con dos tarjetas de tema por fila (156 px) y la de Tablero y piedras pone la vista previa encima de las opciones.
- Las ventanas del escritorio son páginas a pantalla completa con cabecera (flecha atrás + título); las preguntas, una tarjeta centrada sobre un velo oscuro.

## Números de jugada y coordenadas

- Preferencias → Apariencia: coordenadas (escritorio sí, teléfono no, por defecto) y números de jugada (ninguno, las 10 últimas o todas). El número va centrado en la piedra, blanco sobre negras y negro sobre blancas; en la última jugada el aro de acento rodea el número. En un ko, el punto muestra el número más reciente.
- El ancho de la barra lateral del escritorio se recuerda entre sesiones.

## Modo zen y arranque

- Modo zen (Z, menú o menú del teléfono): solo el tablero; se ocultan barra lateral, barras y hojas; fondo quieto; efectos como máximo sutiles. «Modo zen silencioso» (Preferencias) apaga también efectos, música y sonidos salvo el de las piedras. Para salir: Z, el botón «Salir del zen» (35 % de opacidad hasta pasar el puntero) o Atrás en Android.
- Arranque (rediseño 2026-10-18, Mochi): en escritorio, una tarjeta redonda (radio 28, sombra suave) sin barra de título, con el fondo del tema, Kuro y Shiro, «Hoshi» en Fredoka y 星 en el acento, «Go · Baduk · Weiqi», tres piedras que respiran por turnos junto a «Preparando el tablero…» y la versión en una píldora. Las animaciones son de composición (hilo de render), así que siguen moviéndose mientras se construye la ventana principal. En Android, el fondo de la ventana ya es el color nocturno con el icono y el nombre (y en Android 12+ la pantalla de inicio del sistema usa ese color e icono): nunca una pantalla negra o blanca.
- Arrastres en el tablero (flechas, áreas): terminan en la intersección más cercana aunque el puntero se suelte entre dos puntos o un poco fuera del tablero.

## Atajos de teclado (mínimo)

| Atajo | Acción |
|---|---|
| ← / → | Jugada anterior / siguiente |
| Home / End | Inicio / final |
| ↑ / ↓ | Variante anterior / siguiente |
| Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S | Nuevo / abrir / guardar / guardar como |
| Ctrl+E | Modo edición |
| Ctrl+I | Información de la partida |
| P | Pasar (solo partidas locales) |
| T | Territorio actual y potencial |
| A | Mostrar el análisis de la IA (panel y sugerencias); KataGo trabaja siempre de fondo |
| M | Música adaptativa on/off |
| S | Panel de estudio (pins, dibujos, test e informe) |
| Ctrl+L | Jugar en línea |
| F11 | Modo zen *(pendiente)* |

## Pendiente de estilo

- Iconos: barra y herramientas usan `HoshiIcon` (nada de emoji: en Windows ⏮/⏭ salían como emoji de color).
- Sonidos de piedra: Sabaki trae `data/*.mp3` (5 piedras, 5 capturas, pase, nueva partida) dentro de su repo MIT, pero sin origen documentado de las grabaciones; confirmar la procedencia antes de incluirlos, o usar grabaciones CC0.
- Animación al colocar piedra, gráfico de winrate (con motores, fase posterior), modo zen, tema claro.

## Accesibilidad

- Contraste AA en textos del chrome.
- Coordenada de la última jugada en `AutomationProperties.Name` del tablero.
- Todas las funciones accesibles por teclado.

## Accesibilidad y tamaños (2026-10-17)

- Tema **Alto contraste** (`contrast`): cromo negro, texto blanco, acento amarillo `#FFD400`, bordes grises, tablero claro sin sombras, piedras vectoriales y última jugada en rojo; fondo de pizarra estático.
- **Tamaño de la interfaz** (100/115/130/150 %): un `LayoutTransformControl` envuelve la ventana principal y la vista de teléfono; todo escala junto.
- En horizontal (≥ 600 px y más ancho que alto) la hoja del teléfono pasa a la derecha del tablero, con el 40 % del ancho (300–520 px) y sin asa.
- Tarjetas para compartir: degradado oscuro `#1D1F24 → #2E3138`, números en `#F5B041`, texto `#E8E6E3`, marca «Hoshi» opcional en `#8A8F99`.


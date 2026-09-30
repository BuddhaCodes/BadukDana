# Guía visual

Objetivo: la sensación de Sabaki — **el tablero es el protagonista**, la interfaz es oscura, silenciosa y se aparta —
pero con **identidad propia**: Hoshi (星, «estrella») tiene cinco temas, elegibles en *Preferencias* (☰ → Preferencias…,
Ctrl+,), que cambian fondo, paleta, fuentes, iconos, piedras y animaciones al momento.

## Temas (`Themes/HoshiTheme.cs`, aplicados por `ThemeService`)

| Tema | Fondo (propio, animado salvo indicación) | Acento | Fuentes (título · UI) | Iconos | Piedras | Efecto al jugar |
|---|---|---|---|---|---|---|
| **Cielo nocturno** (por defecto) | Azul noche con ~240 estrellas que titilan, nebulosa que deriva, constelaciones tenues y una estrella fugaz cada ~23 s | oro pálido `#D8B46A` | Cormorant Garamond · Manrope | Phosphor light | Perla | Halo dorado |
| **Tinta y oro** | Niebla de tinta sumi-e que respira y un ensō de pincel en oro tenue enmarcando el tablero | oro `#C9A45C`, rojo sello `#B23A2E` | Shippori Mincho · Manrope | Phosphor regular | Pizarra y concha | Onda de tinta |
| **Jardín zen** | Arena rastrillada con ondas alrededor de dos rocas, moviéndose muy despacio | musgo `#9DBB7F` | Zen Kaku Gothic New | Lucide | Pizarra y concha | Onda en la arena |
| **Minimal cálido** | Papel washi con fibras (estático) | terracota `#D07A4A` | Manrope | Tabler | Suaves | Asentarse |
| **Clásico** | Tatami de Sabaki (estático) | `#E0A94A` | Manrope | Lucide | Shudan | — |

- Relojes siempre en JetBrains Mono. Las fuentes van recortadas a latín (+ 星 碁).
- Recursos DynamicResource que cambia el tema: `Bg.*`, `Text.*`, `Accent`, `Danger`, `Border.Subtle`, `Bg.EditBar`,
  `EditBar.Foreground/Hover`, `Bg.Surround`, `Font.UI/Title/Mono`, `Font.Size.Title`, `Icon.*` (21 iconos),
  `Theme.Board` (`BoardStyle`), `Theme.Background`, `Theme.Animations`; también el acento de Fluent.
- «Animaciones» (Preferencias) apaga el fondo animado y las animaciones de piedra; se guarda en `settings.json`.

## Animaciones

- Piedra nueva: aparece 14 % más grande y elevada (sombra mayor) y se asienta en 200 ms (ease-out cúbico); el efecto
  del tema dura 650 ms. Solo cuando aparece exactamente una piedra en la última jugada (jugar o avanzar 1), no al saltar.
- Paneles que aparecen (panel en línea, chat, barra de edición): fundido de 280 ms.
- Botones de la barra: fondo con transición de 150 ms y leve pulsación (escala 0.94). Barra de edición: transición de color.
- Tarjetas de Preferencias: se elevan 3 px al pasar el ratón; borde de acento en la seleccionada.
- Fondo animado a ~24 fps con movimiento muy lento; se detiene fuera de pantalla o con animaciones apagadas.

## Referencia Sabaki

Referencia directa (2026-09-29): Sabaki `d451324` (`style/index.css`) y su componente de tablero Shudan 1.8.0
(`css/goban.css`, `board.png`, `stone_±1.svg`). Ambos MIT; los assets incluidos y sus licencias están en
`THIRD_PARTY_NOTICES.md`. Tomamos colores, medidas y texturas; **no** su código.

## Principios

1. El tablero ocupa todo el espacio disponible sobre un fondo de tatami; la UI se adapta a él.
2. Barra inferior solo bajo el tablero; la barra lateral ocupa toda la altura.
3. Tipografía pequeña y discreta; iconos lineales monocromos.
4. Toda acción importante tiene atajo de teclado (ver abajo).
5. Modo "zen" (F11): solo tablero *(pendiente)*.

## Paleta del tema Clásico (valores por defecto en `Styles/Tokens.axaml`)

| Token | Valor | Origen / uso |
|---|---|---|
| `Bg.Tatami.Base` + `Bg.Tatami` | `#C2CB9C` + `Assets/Sabaki/tatami.png` en mosaico | Fondo alrededor del tablero (Sabaki `main`) |
| `Bg.Bar` | `#292A2D` | Barra inferior (Sabaki `#bar`) |
| `Bg.EditBar` | `#C4BD64` (texto `#222`, hover `#B3AC53`) | Barra en modo edición (Sabaki `.edit #bar`) |
| `Bg.Sidebar` | `#111111` | Barra lateral / árbol (Sabaki `#sidebar`) |
| `Bg.Properties` | `#181818` (texto `#D0D0D0`) | Comentario, chat, panel en línea (Sabaki `#properties`) |
| `Bg.Window` | `#1E1E1E` | Ventanas secundarias (lobby, diálogos) |
| `Border.Subtle` | `#3A3A3A` | Separadores de 1 px |
| `Text.Primary` / `Text.Secondary` | `#E6E6E6` / `#9A9A9A` | Texto |
| `Accent` | `#E0A94A` | Foco, reloj bajo, estado en línea |
| `Danger` | `#D9534F` | Errores |

## Tablero (`GoBoardControl`, `BoardTextures`)

- Fondo `#F1B458` con la textura `board.png` de Shudan cubriendo el tablero (manteniendo proporción).
- Borde de 0.15 celdas `#CA933A`; sombra `0 5px 20px rgba(20,0,15,.8)` sobre el tatami; brillo vertical sutil.
- Líneas, hoshi y coordenadas en `#5E2E0C`; bordes exteriores 1.5×; hoshi de radio 0.1 celda.
- Márgenes: 0.8 celdas sin coordenadas, 1.35 con coordenadas.
- Si la textura no carga, se usa la madera procedural propia.

## Piedras

- Radio 0.46 celda (Shudan: vértice − 0.08 em). Dibujo vectorial de `stone_±1.svg`:
  - Negras: degradado vertical `#443432` → `#0B0B0B`, brillo gris en la mitad superior, borde negro.
  - Blancas: `#FFFFFF` → `#C9D1FF`, reflejo claro en el borde inferior, borde `#C3C3C3`.
- Sombra: disco `rgba(23,10,2,.4)` + sombra `0 .1 .2` celdas del mismo color.
- *Fuzzy placement*: desplazamiento estable por intersección (0.03 celda en 1 de 8 direcciones, o ninguno).
- Piedra fantasma al 40 %; no aparece si la jugada es ilegal.

## Marcadores

- Última jugada: círculo hueco del color contrario (no se dibuja si hay marca SGF en ese punto).
- `TR`, `SQ`, `CR`, `MA`, `LB` (etiqueta con fondo del tablero en intersección vacía).
- Conteo en línea: cruces sobre las piedras muertas.

## Layout de MainWindow

```
┌──────────────────────────────────────────────┬──────────────┐
│ tatami                                       │ (en línea)   │
│        ┌───────────────────────────┐         │ ● Negro 5k   │
│        │          TABLERO          │         │   9:50 + 5×… │
│        │    (centrado, cuadrado)   │         │ ○ Blanco 3k  │
│        └───────────────────────────┘         │ botones      │
│                                              ├──────────────┤
│                                              │ Árbol        │
├──────────────────────────────────────────────┤──────────────┤
│ ⏮ ◀ ▶ ⏭    Negro 5k (●○) Blanco 3k   Pasar Deshacer En línea ☰ │ estado · Jugada │
│                                              │ Comentario / │
│                                              │ Chat         │
└──────────────────────────────────────────────┴──────────────┘
```

- Barra inferior de 40 px bajo el tablero: navegación · jugadores con el interruptor de turno de Sabaki
  (capturas como `+n`) · acciones y menú ☰ (archivo, edición, en línea).
- En modo edición la barra se vuelve `#C4BD64` y muestra las herramientas.
- Barra lateral de 280 px (redimensionable, 220–420): panel en línea (solo en partidas de OGS), árbol,
  encabezado con estado y número de jugada, comentario o chat.

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
- **Celebración de jugadas fuertes** (solo con análisis activo, al jugar; no al navegar). Llega cuando KataGo termina de valorar la jugada:
  - *Buena* (fuerza 1): golpe seco (`impact_small`) y una onda pequeña.
  - *Excelente* (2): explosión (`explosion_medium`), destello, onda expansiva, chispas, temblor suave y un cráter pequeño con 5 fracturas.
  - *La mejor* (3): explosión cinemática (`explosion_big`: caída de subgraves, «braam» grave, cola de escombros y reverberación), destello grande, dos ondas, ~56 chispas con estela, temblor fuerte y **suelo dañado**: cráter irregular con fondo astillado y labio levantado, 8 fracturas radiales en facetas (anchas en el cráter, afinándose hasta un hilo, con bifurcaciones), fracturas en anillo que las unen y astillas de madera despedidas. Relieve con luz desde arriba a la izquierda (halo oscuro de oclusión y labio claro abajo a la derecha). Las fracturas nacen incandescentes (metal fundido) y se enfrían a grietas oscuras; todo se desvanece en ~3,2 s.
  - Las grietas se dibujan bajo las piedras y recortadas al tablero; el resto encima. Todo determinista a partir de una semilla (`ImpactEffect`). Sin animaciones (Apariencia) solo suena.

## Música adaptativa

- Lo-fi tranquilo (Dm9 – G13 – Cmaj9 – Am9, 80 BPM, vinilo y cinta). Capas: 0 piano eléctrico (siempre), 1 bajo y bombo, 2 batería con swing, 3 arpegio pulsado con eco, 4 «hype» (acordes con bombeo, hi-hats a semicorcheas, palmas, melodía brillante).
- Calor: buena +0,6, excelente +0,9, la mejor +1,2, más +0,15 por jugada de racha (≤ 25 s entre buenas, máx. +0,6). Imprecisa −1, error −2, error grave → 0 con tape-stop. Tras 12 s sin buenas jugadas se enfría 0,06/s.
- Cada capa entra ~0,9 de calor después de la anterior; el filtro se abre de 900 Hz (apagado, acogedor) a ~18 kHz.

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
| A | Análisis con IA (KataGo) |
| M | Música adaptativa on/off |
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

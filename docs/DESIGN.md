# Guía visual (inspirada en Sabaki)

Objetivo: la sensación de Sabaki — **el tablero es el protagonista**, la interfaz es oscura, silenciosa y se aparta.

Referencia directa (2026-09-29): Sabaki `d451324` (`style/index.css`) y su componente de tablero Shudan 1.8.0
(`css/goban.css`, `board.png`, `stone_±1.svg`). Ambos MIT; los assets incluidos y sus licencias están en
`THIRD_PARTY_NOTICES.md`. Tomamos colores, medidas y texturas; **no** su código.

## Principios

1. El tablero ocupa todo el espacio disponible sobre un fondo de tatami; la UI se adapta a él.
2. Barra inferior solo bajo el tablero; la barra lateral ocupa toda la altura.
3. Tipografía pequeña y discreta; iconos lineales monocromos.
4. Toda acción importante tiene atajo de teclado (ver abajo).
5. Modo "zen" (F11): solo tablero *(pendiente)*.

## Paleta (tokens en `Styles/Tokens.axaml`)

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
| Ctrl+L | Jugar en línea |
| F11 | Modo zen *(pendiente)* |

## Pendiente de estilo

- Sonidos de piedra: Sabaki trae `data/*.mp3` (5 piedras, 5 capturas, pase, nueva partida) dentro de su repo MIT, pero sin origen documentado de las grabaciones; confirmar la procedencia antes de incluirlos, o usar grabaciones CC0.
- Animación al colocar piedra, gráfico de winrate (con motores, fase posterior), modo zen, tema claro.

## Accesibilidad

- Contraste AA en textos del chrome.
- Coordenada de la última jugada en `AutomationProperties.Name` del tablero.
- Todas las funciones accesibles por teclado.

# Después de la 1.0 — diseños (2026-10-17)

> Propuestas para las tareas 26–29 de `ROADMAP.md`. Nada de esto está programado: cada una necesita una decisión del usuario (arquitectura, privacidad o términos de terceros) antes de empezar.

## 26. Sincronizar estudios y progreso entre escritorio y teléfono

**Qué se sincroniza**: la biblioteca de partidas (`replays/`), los estudios (van dentro del SGF), el progreso de josekis y problemas (`joseki/progress.json`, `tsumego-progress.json`, `tsumego/from-my-games.sgf`), `reviews.json` y las preferencias que tienen sentido en ambos (idioma, tema, números de jugada). **No** se sincronizan tokens de OGS ni rutas de KataGo.

**Opciones** (de menos a más infraestructura):

1. **Carpeta sincronizada por el usuario** (Syncthing, Dropbox, Google Drive, OneDrive): Hoshi permite elegir la carpeta de datos. Sin servidor ni cuentas. Riesgo: dos equipos escriben a la vez → hace falta fusión por archivo.
2. **Exportar/importar un paquete** (`.hoshi` = zip): manual, sin conflictos en vivo; sirve también como copia de seguridad.
3. **Servicio propio de Hoshi**: cuentas, cifrado, coste y responsabilidad de datos personales. No recomendado para un proyecto sin servidor.

**Recomendación**: 2 primero (rápido, útil como copia de seguridad) y después 1.

**Fusión** (necesaria para 1):
- Partidas: unión por id, gana la de `Played` más reciente.
- Estudios: `StudyStore.Merge` ya existe.
- Tarjetas de Leitner: por id, gana la de última revisión más reciente.

**Decisiones del usuario**: qué opción elegir y si las preferencias viajan.

## 27. Más servidores (KGS, Fox)

- **KGS**: protocolo JSON sobre HTTP de larga espera («KGS JSON API», `kgs-json`), documentado y permitido para clientes. Encaja como `Hoshi.Kgs` paralelo a `Hoshi.Ogs`, con los mismos eventos de dominio (jugada, reloj, fase, chat) y el mismo `OnlineGameViewModel`.
  - Cuidado: el reloj de KGS es por canal.
  - Cuidado: el conteo usa marcado de muertas como OGS, pero con otros mensajes.
- **Fox (野狐)**: no tiene API pública; los clientes de terceros van contra sus términos. **No implementar** salvo permiso escrito.
- **IGS/Pandanet**: protocolo telnet de texto. Es posible, pero de poco uso y frágil.

**Antes de programar**:
- Leer los términos actuales de cada servidor.
- Usar fixtures grabados en una cuenta de pruebas, como con OGS en beta.
- Mantener la regla de no usar IA en partidas en curso.

## 28. iOS

- Avalonia 11 soporta iOS (`net10.0-ios`, `Avalonia.iOS`). La vista de teléfono (`PhoneView` y `PhoneNavigation`) se reutiliza tal cual.
- **KataGo**: Apple no permite lanzar procesos. Hay dos caminos:
  1. Enlazar KataGo como biblioteca y llamarlo en el mismo proceso (`dlopen` no vale; hace falta un `xcframework` estático y una envoltura C para la entrada y salida JSON).
  2. Dejar el análisis fuera en iOS.

  Es el punto más caro del port.
- **Firma y distribución**: hacen falta una cuenta de Apple Developer, TestFlight y la revisión de la App Store. El login de OGS por OAuth necesita un esquema de URL propio.
- **Audio**: `AVAudioEngine` detrás de `PcmSinks.PlatformFactory`, como `AudioTrackSink` en Android.
- **Recomendación**: empezar con la versión sin KataGo (editor, estudio, OGS, problemas) y medir el interés.

## 29. Modo profesor: tablero compartido en vivo con alumnos

**Objetivo**: el profesor mueve, dibuja y anota, y los alumnos ven lo mismo en directo. El profesor puede pasarle el control a un alumno.

**Transporte**, sin servidor propio:
- **a)** Las salas de revisión («review») de OGS, que ya hacen esto y que Hoshi puede abrir. Es la opción más barata: el protocolo de revisión de OGS está en `goban` (`review/*`).
- **b)** WebRTC punto a punto, con un servidor de señalización mínimo.
- **c)** Red local (UDP multicast para descubrir, TCP para el estado), para clubes y aulas.

**Modelo**: los cambios del árbol se describen como operaciones que se pueden repetir en orden:
- `play`
- `goto`
- `markup`
- `drawing`
- `comment`

Los estudios ya guardan las anotaciones de forma serializable en el nodo, lo que facilita **c**.

**Privacidad**:
- Los alumnos solo ven el nombre que elija el profesor.
- Nada se graba sin avisar.

**Recomendación**: **a** primero (las revisiones de OGS en el lobby y el control del profesor), y **c** para clases presenciales.

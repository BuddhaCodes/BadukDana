# Third-party notices

Hoshi bundles the following third-party assets. Their licenses are reproduced below or in `licenses/`.

Since 2026-10-17 no image of Sabaki or Shudan is bundled (the Classic theme, its tatami and Shudan's board texture were
removed); every board, stone and background is Hoshi's own.

### Fonts (SIL Open Font License 1.1)

Bundled in `src/Hoshi.App/Assets/Fonts/`, taken from the `@expo-google-fonts/*` npm packages (Google Fonts builds),
**subset** to Latin (plus 星 and 碁) with fontTools and with their name tables unified per family so that all weights
load as one family. None of them declares a Reserved Font Name. Full license texts: `licenses/OFL-*.txt`.

| Family | Copyright |
|---|---|
| Manrope | © 2018 The Manrope Project Authors |
| Cormorant Garamond | © 2015 the Cormorant Project Authors |
| Shippori Mincho | © 2021 The Shippori Mincho Project Authors |
| Zen Kaku Gothic New | © 2022 The Zen Kaku Gothic Project Authors |
| JetBrains Mono | © 2020 The JetBrains Mono Project Authors |

### Fonts of the promotional page (SIL Open Font License 1.1)

Served from `docs/site/fonts/`, copied unmodified from the `@fontsource/*` 5.3.0 npm packages (Latin subsets, plus the two
Shippori Mincho B1 Japanese subsets that hold 星 and 正). `shippori-mincho-700-jo-chu-shu.woff2` is a subset of the app's
Shippori Mincho Bold with only 序, 中 and 終 (made with fontTools; the OFL allows modified versions under the same
licence). No Reserved Font Name. Texts: `licenses/OFL-Fraunces.txt`, `licenses/OFL-Figtree.txt`,
`licenses/OFL-ShipporiMinchoB1.txt`, `licenses/OFL-ShipporiMincho.txt`, `licenses/OFL-JetBrainsMono.txt`.

| Family | Copyright |
|---|---|
| Fraunces | © 2020 The Fraunces Project Authors |
| Figtree | © 2022 The Figtree Project Authors |
| Shippori Mincho B1 | © 2021 The Shippori Mincho Project Authors |
| Shippori Mincho (subset: 序 中 終) | © 2021 The Shippori Mincho Project Authors |
| JetBrains Mono | © 2020 The JetBrains Mono Project Authors |

### KataGo (bundled in the Windows and Linux downloads, or downloaded by "Install KataGo")

| Component | Source | License | Text |
|---|---|---|---|
| KataGo v1.17.1 (OpenCL build, `katago`/`katago.exe`, `cacert.pem`) | github.com/lightvector/KataGo releases, unmodified | MIT, © David J Wu ("lightvector") and contributors; parts under the licenses in its `cpp/external` | `licenses/MIT-KataGo.txt` |
| Network `g170e-b10c128-s1141046784-d204142634` (`engines/katago/b10c128.txt.gz`) | KataGo project, g170 run (katagoarchive.org) | Published by the KataGo project for use with KataGo; see katagotraining.org for its terms | — |
| OpenSSL 3 (`libcrypto-3-x64.dll`, `libssl-3-x64.dll`, Windows) | shipped inside KataGo's Windows build | Apache-2.0 | `licenses/Apache-OpenSSL.txt` |
| libzip (`zip.dll`, Windows) | shipped inside KataGo's Windows build | BSD-3-Clause | `licenses/BSD-libzip.txt` |
| zlib (`z.dll`, Windows) | shipped inside KataGo's Windows build | zlib | `licenses/Zlib-zlib.txt` |
| bzip2 (`bz2.dll`, Windows) | shipped inside KataGo's Windows build | bzip2 (BSD-style) | `licenses/bzip2.txt` |
| Microsoft Visual C++ runtime (`msvcp140*.dll`, `vcruntime140*.dll`, Windows) | shipped inside KataGo's Windows build | Microsoft Visual C++ Redistributable terms | — |

### Android (Hoshi-android.apk)

| Component | Source | License | Text |
|---|---|---|---|
| KataGo v1.17.1 (CPU/Eigen build for arm64, `libkatago.so`) | built from github.com/lightvector/KataGo sources, unmodified, by `tools/android/build-katago.sh` | MIT, © David J Wu ("lightvector") and contributors; parts under the licenses in its `cpp/external` | `licenses/MIT-KataGo.txt` |
| Eigen 3.4.0 (header-only, compiled into `libkatago.so`) | gitlab.com/libeigen/eigen, unmodified | MPL-2.0 (source at gitlab.com/libeigen/eigen) | `licenses/MPL-2.0-Eigen.txt` |
| zlib, LLVM libc++ (static) | Android NDK | zlib; Apache-2.0 with LLVM exception | `licenses/Zlib-zlib.txt` |
| Network `g170e-b10c128` | as above | as above | — |
| Avalonia.Android and AndroidX libraries | NuGet (Avalonia, Microsoft/Google) | MIT; Apache-2.0 | — |

### Installers and updates (Velopack)

| Component | Where | License | Text |
|---|---|---|---|
| Velopack 1.2.161 (`Velopack.dll`; the `Update.exe` / `UpdateMac` / `UpdateNix` helpers and `Setup.exe` added by `vpk`) | every installer and update package | MIT, © 2021 Caelan Sayler, © 2024 Velopack Ltd. | `licenses/MIT-Velopack.txt` |
| AppImage type2-runtime (the start-up code of the Linux `.AppImage`) | Linux `.AppImage` | MIT, © 2004-23 probonopd; it links libfuse (LGPL-2.0, github.com/libfuse/libfuse), squashfuse (BSD-2, github.com/vasi/squashfuse), libzstd (BSD-3) and zlib (zlib) | `licenses/MIT-AppImage-type2-runtime.txt` |

`vpk` also uses zstd, mksquashfs and (for MSI, not used here) WiX while building; they are not shipped.

### Icons

Converted to path data by `tools/gen_icons.py` into `src/Hoshi.App/Themes/IconSets.g.cs`.

| Set | Version | License | Text |
|---|---|---|---|
| Phosphor Icons (light, regular) | @phosphor-icons/core 2.1.1 | MIT, © 2023 Phosphor Icons | `licenses/MIT-Phosphor.txt` |
| Lucide | lucide-static 1.49.0 | ISC, © Lucide Icons and Contributors (includes Feather, MIT) | `licenses/ISC-Lucide.txt` |
| Tabler Icons (outline) | @tabler/icons 3.48.0 | MIT, © 2020-2026 Paweł Kuna | `licenses/MIT-Tabler.txt` |

### Hoshi's own art

The night sky, ink wash and ensō, raked sand, washi paper, the procedural kaya wood and the Pearl / Slate & shell /
Soft stones are generated in code by Hoshi (`ThemeBackground`, `BoardTextures`) and need no attribution.

The sound effects in `src/Hoshi.App/Assets/Sounds/` (`impact_small`, `explosion_medium`, `explosion_big`) are
synthesised by `tools/gen_sounds.py` (noise, oscillators and filters; no samples) and are Hoshi's own. Sounds a user
places in their own `sounds/` folder are not distributed with Hoshi.

The app icon (`src/Hoshi.App/Assets/hoshi.ico`, `hoshi.png`) is drawn by `tools/gen_icon.py` and is Hoshi's own.

### Shudan

Hoshi's "classic gradient" vector stones reproduce the colours and gradients of Shudan's `stone_1.svg` /
`stone_-1.svg`, and the default line colour is Shudan's (MIT license below). No Sabaki or Shudan source code or image is
included.

NuGet dependencies are consumed as packages under their own licenses (see `Directory.Packages.props`).

---

## Shudan

```
MIT License

Copyright (c) 2018-2022 Yichuan Shen

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```


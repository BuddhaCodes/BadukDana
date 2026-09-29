# Third-party notices

Hoshi bundles the following third-party assets. Their licenses are reproduced below or in `licenses/`.

| File | Origin | License |
|---|---|---|
| `src/Hoshi.App/Assets/Sabaki/board.png` | Board texture of [@sabaki/shudan](https://github.com/SabakiHQ/Shudan) 1.8.0 (`css/board.png`) | MIT, © 2018-2022 Yichuan Shen |
| `src/Hoshi.App/Assets/Sabaki/tatami.png` | Background texture of [Sabaki](https://github.com/SabakiHQ/Sabaki) (`img/ui/tatami.png`, commit d451324) | MIT, © 2015-2020 Yichuan Shen |

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

### Sabaki / Shudan

Hoshi's stone rendering reproduces the colours and gradients of Shudan's `stone_1.svg` / `stone_-1.svg`
and the board colours of Shudan's `goban.css` (same MIT license). No Sabaki or Shudan source code is included.

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

## Sabaki

```
The MIT License (MIT)

Copyright (c) 2015-2020 Yichuan Shen

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

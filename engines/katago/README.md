# KataGo shipped with Hoshi

Hoshi runs [KataGo](https://github.com/lightvector/KataGo) for its analysis, move effects and adaptive music.

- **Program** — not stored here. The release workflow downloads KataGo's official OpenCL build
  (`v1.17.1`, Windows x64 and Linux x64) from KataGo's GitHub releases, checks its SHA-256 and puts it in the
  `katago/` folder of the Windows and Linux downloads. The same files are what "Install KataGo" downloads inside the
  app (`src/Hoshi.App/Services/KataGo/KataGoSetup.cs` pins URLs, sizes and hashes). macOS has no official build:
  `brew install katago`, and Hoshi uses it.
- **Network** — `b10c128.txt.gz`: KataGo's `g170e-b10c128-s1141046784-d204142634` network (10 blocks, 128
  channels) from the g170 run, gzipped. Small and quick, also on integrated GPUs. The release workflow puts it next
  to KataGo in the downloads and publishes it as the release asset `Hoshi-katago-b10c128.txt.gz`, which "Install
  KataGo" downloads (SHA-256 pinned in `KataGoSetup.cs`). Any other network can be chosen in Preferences → Analysis.

Licences: see `THIRD_PARTY_NOTICES.md` (KataGo: MIT; the libraries in KataGo's Windows build keep their own
licences).

# Hoshi

Cross-platform desktop client for Baduk / Go / Weiqi, built with **Avalonia** on **.NET 10**.
Local play and SGF editing with a Sabaki-inspired look, and online play through **OGS** (online-go.com).

> Status: Phase 0 (skeleton). See `docs/ROADMAP.md`.

## Requirements

- .NET SDK 10.0 (`global.json` pins the minimum feature band 10.0.100)

## Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/Hoshi.App
```

Logs are written to the per-user data folder (`%LOCALAPPDATA%\Hoshi\logs` on Windows,
`~/Library/Application Support/Hoshi/logs` on macOS, `~/.local/share/Hoshi/logs` on Linux).

## Layout

```
src/
  Hoshi.Core/     Pure Go rules (no UI, no I/O)
  Hoshi.Sgf/      SGF parser/serializer and game tree
  Hoshi.Ogs/      online-go.com client
  Hoshi.Engines/  GTP engines (later phase)
  Hoshi.App/      Avalonia application
tests/            xUnit + FluentAssertions, Avalonia.Headless for UI
docs/             Architecture, design, OGS API notes, roadmap
```

Dependency rule: `App → Ogs, Sgf, Core` · `Ogs → Core` · `Sgf → Core` · `Core → nothing`
(enforced by `tests/Hoshi.App.Tests/ArchitectureTests.cs`).

## Development against OGS

Pick the server in the lobby (**Ctrl+L** or "En línea"); it does not depend on the .NET environment.

| Server | Sign-in |
|---|---|
| `https://online-go.com` (default) | "Continuar con Google" or "Iniciar sesión con OGS": browser OAuth (authorization code + PKCE, public client, redirect `http://127.0.0.1:8734/callback`). |
| `https://beta.online-go.com` (pruebas) | Username + password (beta cannot register OAuth apps). The password is never stored. |

```bash
dotnet run --project src/Hoshi.App
```

"Continuar con Google" always uses online-go.com. There: unranked, private games against your own second
account only. Hoshi never creates or accepts ranked games. Set `Ogs:DefaultServer` to `beta` in `appsettings.json`
to start on beta. No secrets live in the repository: the OAuth client id is public.

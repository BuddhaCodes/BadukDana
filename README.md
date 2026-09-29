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

| Environment | Server | Sign-in |
|---|---|---|
| `Development` | `https://beta.online-go.com` | Username + password (beta cannot register OAuth apps). The password is never stored. |
| `Production` (default) | `https://online-go.com` | Browser OAuth (authorization code + PKCE, public client, redirect `http://127.0.0.1:8734/callback`). |

```bash
# Beta, for everyday development
DOTNET_ENVIRONMENT=Development dotnet run --project src/Hoshi.App      # PowerShell: $env:DOTNET_ENVIRONMENT="Development"
# online-go.com (OAuth) — unranked, private games against your own second account only
dotnet run --project src/Hoshi.App
```

Visual Studio / Rider: pick the launch profile "Hoshi (beta, desarrollo)" or "Hoshi (online-go.com)".
Open the lobby with **Ctrl+L** (or "En línea" in the bottom bar). Hoshi never creates or accepts ranked games.
No secrets live in the repository: the OAuth client id is public.

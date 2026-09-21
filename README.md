# <img width="3072" height="1376" alt="BannerLogo" src="https://github.com/user-attachments/assets/4e8a811a-0d18-4914-9b9c-e270c1aed014" />

# 🏆 TrophyPrompt

**Edit PS3 trophy timestamps without the headache.** Open a trophy folder, export to JSON, let an LLM generate realistic unlock times, import back, save. Done.

[![Build](https://github.com/TYFALY/TrophyPrompt/actions/workflows/ci.yml/badge.svg)](https://github.com/TYFALY/TrophyPrompt/actions/workflows/ci.yml)
[![Release](https://github.com/TYFALY/TrophyPrompt/actions/workflows/release.yml/badge.svg)](https://github.com/TYFALY/TrophyPrompt/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Avalonia 11](https://img.shields.io/badge/Avalonia-11-blue.svg)](https://avaloniaui.net/)

Built with Avalonia 11 on .NET 8. Ported from [PS3TrophyIsGood](https://github.com/darkautism/PS3TrophyIsGood).

---

## Why this exists

PS3 trophy timestamps are stored in encrypted PFD files. Editing them manually is painful. TrophyPrompt makes it boringly simple:

| Step | What you do |
|------|-------------|
| 1. **Open** | Pick a trophy folder (e.g. `NPWR03468_00`) — app decrypts `TROPTRNS.DAT` / `TROPUSR.DAT` |
| 2. **Export** | Get a JSON file with game title, title ID, account ID, full trophy list, and a system prompt |
| 3. **Generate** | Paste JSON into Claude/GPT → get back realistic, chronological timestamps |
| 4. **Import** | Matches by trophy ID (falls back to name), applies unlocks + times |
| 5. **Save** | Re-encrypts and re-signs the PFD |

<div align="center">
  <img width="800" height="529" alt="TrophyPrompt UI" src="https://github.com/user-attachments/assets/91ab2190-b62c-45ab-b93b-d60453c3f19e" />
</div>

---

## Quick Start (60 seconds)

```bash
# Clone & build
git clone https://github.com/TYFALY/TrophyPrompt.git
cd TrophyPrompt
dotnet build TrophyPrompt.sln -c Release

# Run
dotnet run --project TrophyPrompt.csproj -c Release
```

**Prefer a standalone executable?** Grab the latest from [Releases](https://github.com/TYFALY/TrophyPrompt/releases) — single file, no .NET install needed.

---

## Features

- **Cross-platform** — Windows, macOS (Intel + Apple Silicon), Linux
- **Zero external deps** — `TROPHYParser`, `BigEndianTool`, `pfdtool` all vendored in `/vendor`
- **Smart import** — Matches by trophy ID, falls back to name, preserves existing timestamps
- **LLM-ready export** — JSON includes system prompt that enforces chronological order, platinum last
- **Batch toolbar** — Edit multiple trophies at once
- **Animated wave header** — Because life's too short for boring UIs

---

## Building from source

Requires .NET 8 SDK. All dependencies vendored — no sibling checkouts needed.

```bash
# Standard build (current platform)
dotnet build TrophyPrompt.sln -c Release

# Single-file self-contained executables
dotnet publish TrophyPrompt.csproj -c Release -r win-x64   --self-contained true -p:PublishSingleFile=true -o ./publish/win-x64
dotnet publish TrophyPrompt.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish/linux-x64
dotnet publish TrophyPrompt.csproj -c Release -r osx-x64   --self-contained true -p:PublishSingleFile=true -o ./publish/osx-x64
dotnet publish TrophyPrompt.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o ./publish/osx-arm64
```

> **Note:** `pfdtool` (PFD encryption/decryption) is Windows-only. On macOS/Linux the app launches but decryption/encryption won't work unless you provide a cross-platform alternative or run the Windows binary via Wine.

---

## Automated Releases

Push a semver tag — GitHub Actions handles the rest:

```bash
git tag v1.0.0
git push origin v1.0.0
```

Pipeline builds all four platforms, packages as `.tar.gz` (Unix) / `.zip` (Windows), generates changelog from commits, attaches artifacts to the release.

---

## Project Structure

```
TrophyPrompt/
├── Views/           # MainWindow, dialogs, wave animation
├── ViewModels/      # Main logic: open/save/export/import, row editing
├── Models/          # TrophyDto, ExportRootDto (with system prompt)
├── Core/            # TrophyUtility — pfdtool calls, temp folder handling
├── vendor/
│   ├── TROPHYParser/    # PS3 trophy parsing
│   ├── BigEndianTool/   # Big-endian binary I/O
│   └── pfdtool/         # Native PFD crypto (Windows exe + configs)
└── TrophyPrompt.sln
```

---

## Contributing

Help is welcome — seriously. This is a niche tool and every PR matters.

**Good places to start:**
- Cross-platform `pfdtool` alternative (biggest gap — see note above)
- Linux/macOS packaging (AppImage, DMG, Flatpak)
- Unit tests for the parser layer
- UI polish / accessibility improvements

**Quick contributing flow:**
1. Fork → branch → PR
2. `dotnet build` and `dotnet format` must pass
3. Describe the change; screenshots for UI work

See [CONTRIBUTING.md](CONTRIBUTING.md) if it exists, otherwise just open a PR.

---

## License & Credits

**MIT License** — see [LICENSE](LICENSE) for details.

- Original PS3 trophy parsing logic: [PS3TrophyIsGood](https://github.com/darkautism/PS3TrophyIsGood) by darkautism
- `pfdtool` by flatz (included in `/vendor/pfdtool`)
- Avalonia UI framework
- CommunityToolkit.Mvvm, Newtonsoft.Json, SkiaSharp, HarfBuzzSharp

> Only mess with trophy folders you own. PlayStation is a trademark of Sony; this project is not affiliated with or endorsed by them.
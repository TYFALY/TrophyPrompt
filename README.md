# <img width="3072" height="1376" alt="BannerLogo" src="https://github.com/user-attachments/assets/4e8a811a-0d18-4914-9b9c-e270c1aed014" />
# 🏆 TrophyPrompt

PS3 trophy timestamp editor for Windows, macOS, and Linux. Open a trophy folder, export it to JSON, let an LLM fill in realistic unlock times, import it back, save. That is the whole program.

Built with Avalonia 11 on .NET 8, ported from [PS3TrophyIsGood](https://github.com/darkautism/PS3TrophyIsGood).

## The workflow

1. **Open Folder** — pick a PS3 trophy directory (something like `NPWR03468_00`). The app decrypts `TROPTRNS.DAT` / `TROPUSR.DAT` with pfdtool.
2. **Export JSON** — writes one file with the game title, title ID, account ID, the full trophy list, and a system prompt footer. The prompt sets the rules: don't rename anything, don't touch existing timestamps, keep everything chronological, platinum pops last.
3. **Paste the JSON into Claude or GPT** and take the timestamps it returns.
4. **Import JSON** — matches entries by trophy ID (falls back to name) and applies the unlocks and times. Old plain-array exports still import fine.
5. **Save** — writes the data back and re-signs the PFD.

<div align="center">
  <img width="800" height="529" alt="TrophyPrompt" src="https://github.com/user-attachments/assets/91ab2190-b62c-45ab-b93b-d60453c3f19e" />
</div>

## Building it

Requires the .NET 8 SDK.

All external dependencies are vendored internally under `/vendor`:
- `vendor/TROPHYParser` — PS3 trophy data parsing
- `vendor/BigEndianTool` — Big-endian binary reading/writing
- `vendor/pfdtool` — Native PS3 PFD encryption/decryption tool (Windows only)

### Cross-platform build (Windows, macOS, Linux)

```bash
# Clone the repository
git clone https://github.com/TYFALY/TrophyPrompt.git
cd TrophyPrompt

# Restore dependencies
dotnet restore

# Build for current platform
dotnet build TrophyPrompt.sln -c Release

# Run the application
dotnet run --project TrophyPrompt.csproj -c Release
```

### Single-file self-contained executables

#### Windows x64
```bash
dotnet publish TrophyPrompt.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ./publish/win-x64
```
Output: `publish/win-x64/TrophyPrompt.exe` (~79 MB, no .NET runtime required)

#### Linux x64
```bash
dotnet publish TrophyPrompt.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ./publish/linux-x64
```
Output: `publish/linux-x64/TrophyPrompt`

#### macOS x64 (Intel)
```bash
dotnet publish TrophyPrompt.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ./publish/osx-x64
```
Output: `publish/osx-x64/TrophyPrompt`

#### macOS ARM64 (Apple Silicon)
```bash
dotnet publish TrophyPrompt.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ./publish/osx-arm64
```
Output: `publish/osx-arm64/TrophyPrompt`

> **Note:** The `pfdtool` native executable is Windows-only. On macOS and Linux, the application will launch but trophy decryption/encryption operations will not function unless a compatible cross-platform alternative is provided or the Windows binary is run via Wine.

### Automated Releases

Releases are created automatically by pushing a semantic version tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This triggers the GitHub Actions release pipeline which:
1. Builds self-contained single-file binaries for Windows (x64), Linux (x64), macOS (x64), and macOS (ARM64)
2. Packages each as `.tar.gz` (Unix) or `.zip` (Windows)
3. Creates a GitHub Release with auto-generated changelog from commit history
4. Attaches all platform artifacts to the release

## What's inside

- `Views/MainWindow.axaml` — trophy grid, search/filter bar, batch toolbar, animated wave header
- `ViewModels/MainViewModel.cs` — open, save, export, import, row editing
- `Models/TrophyDto.cs` — the trophy record plus `ExportRootDto`, the export wrapper that carries the system prompt
- `Core/TrophyUtility.cs` — pfdtool decrypt/encrypt calls and temp-folder handling
- `vendor/` — vendored `TROPHYParser`, `BigEndianTool`, and `pfdtool` (no external checkout needed)

## Notes

- pfdtool and its key files (`games.conf`, `global.conf`) ship in the output folder, along with `msvcr100.dll` (Windows only).
- Only mess with trophy folders you own. PlayStation is a trademark of Sony; this project is not affiliated with or endorsed by them.
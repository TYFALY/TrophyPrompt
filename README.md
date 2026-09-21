# <img width="3072" height="1376" alt="BannerLogo" src="https://github.com/user-attachments/assets/4e8a811a-0d18-4914-9b9c-e270c1aed014" />
# 🏆 TrophyPrompt

PS3 trophy timestamp editor for Windows. Open a trophy folder, export it to JSON, let an LLM fill in realistic unlock times, import it back, save. That is the whole program.


Built with Avalonia 11 on .NET 8, ported from [PS3TrophyIsGood](https://github.com/darkautism/PS3TrophyIsGood).

## The workflow

1. **Open Folder** — pick a PS3 trophy directory (something like `NPWR03468_00`). The app decrypts `TROPTRNS.DAT` / `TROPUSR.DAT` with pfdtool.
2. **Export JSON** — writes one file with the game title, title ID, account ID, the full trophy list, and a system prompt footer. The prompt sets the rules: don't rename anything, don't touch existing timestamps, keep everything chronological, platinum pops last.
3. **Paste the JSON into Claude or GPT** and take the timestamps it returns.
4. **Import JSON** — matches entries by trophy ID (falls back to name) and applies the unlocks and times. Old plain-array exports still import fine.
5. **Save** — writes the data back and re-signs the PFD.

## Building it

Requires the .NET 8 SDK on Windows.

### From the TrophyPrompt folder (standalone)

```powershell
cd TrophyPrompt
dotnet build TrophyPrompt.sln -c Release
```

### Single-file self-contained executable (Windows x64)

```powershell
cd TrophyPrompt
dotnet publish TrophyPrompt.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The exe lands in `TrophyPrompt/publish/TrophyPrompt.exe` (~79 MB, no .NET runtime required).

### From the repository root (full solution)

```powershell
dotnet build PS3TrophyIsGood.sln -c Release
```

## What's inside

- `Views/MainWindow.axaml` — trophy grid, search/filter bar, batch toolbar, animated wave header
- `ViewModels/MainViewModel.cs` — open, save, export, import, row editing
- `Models/TrophyDto.cs` — the trophy record plus `ExportRootDto`, the export wrapper that carries the system prompt
- `Core/TrophyUtility.cs` — pfdtool decrypt/encrypt calls and temp-folder handling
- `Dependencies/` — vendored `TROPHYParser` and `BigEndianTool` (no external checkout needed)

## Notes

- pfdtool and its key files (`games.conf`, `global.conf`) ship in the output folder, along with `msvcr100.dll`.
- Only mess with trophy folders you own. PlayStation is a trademark of Sony; this project is not affiliated with or endorsed by them.
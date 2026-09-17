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

```powershell
dotnet build TrophyPrompt\TrophyPrompt.csproj -c Release
```

Single-file exe:

```powershell
dotnet publish TrophyPrompt\TrophyPrompt.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The exe lands in `bin\Release\net8.0\win-x64\publish\TrophyPrompt.exe`.

The repo is self-contained: `TrophyPrompt.sln` builds everything, with `TROPHYParser` and `BigEndianTool` vendored under `lib/` (both MIT, darkautism) and pfdtool in `lib/pfdtool`.

## Paradox check

Before every Save, the trophy set is validated:

- locked trophies must not carry a timestamp (stale ones are auto-cleared on Save, same as the legacy lock path)
- nothing may predate the PS3 launch (2006-11-11)
- an unlocked platinum must be the latest unlock
- same-group unlocks must be in list order

A failing set blocks Save (and Save As) with the reason in the status bar, and the offending rows turn red — hover one for the exact reason. Export still works with paradoxes present (export is how you get AI help to fix them) but warns in the notice file.

## What's inside

- `TrophyPrompt.sln` — builds the app plus vendored libs
- `lib/` — vendored `TROPHYParser`, `BigEndianTool`, pfdtool
- `Views/MainWindow.axaml` — trophy grid, search/filter bar, batch toolbar, animated wave header
- `ViewModels/MainViewModel.cs` — open, save, export, import, row editing, paradox validation
- `Models/TrophyDto.cs` — the trophy record plus `ExportRootDto`, the export wrapper that carries the system prompt
- `Models/ParadoxValidator.cs` — the chronological validation rules
- `Core/TrophyUtility.cs` — pfdtool decrypt/encrypt calls and temp-folder handling

## Notes

- pfdtool and its key files (`games.conf`, `global.conf`) ship in the output folder, along with `msvcr100.dll`.
- Only mess with trophy folders you own. PlayStation is a trademark of Sony; this project is not affiliated with or endorsed by them.

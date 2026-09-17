# 🏆 TrophyPrompt

PS3 trophy timestamp editor for Windows. Open a trophy folder, export it to JSON, let an LLM fill in realistic unlock times, import it back, save. That is the whole program.

Built with Avalonia 11 on .NET 8, ported from [PS3TrophyIsGood](https://github.com/darkautism/PS3TrophyIsGood).

## The workflow

1. **Open Folder** — pick a PS3 trophy directory (something like `NPWR03468_00`). The app decrypts it with pfdtool.
2. **Export JSON** — writes one file containing the game title, title ID, account ID, the full trophy list, and a system prompt footer. The prompt tells the model the rules: don't rename anything, don't touch existing timestamps, keep everything chronological, platinum pops last.
3. **Paste the JSON into Claude or GPT** and take the timestamps it returns.
4. **Import JSON** — matches entries by trophy ID (falls back to name) and applies the unlocks and times.
5. **Save** — writes the data back and re-signs the PFD so the PS3/RPCS3 accepts it.

Old plain-array exports still import fine.

## Building it

Requires the .NET 8 SDK on Windows.

```powershell
dotnet build TrophyPrompt\TrophyPrompt.csproj -c Release
```

Single-file exe:

```powershell
dotnet publish TrophyPrompt\TrophyPrompt.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

One catch: a bare clone of this repo does not build on its own. The project file points at sibling folders (`..\TROPHYParser`, `..\BigEndianTool`, `..\PS3TrophyIsGood\pfdtool`) that live next to it in the original checkout. Either clone the full layout or vendor those three in and fix the paths.

## What's inside

- `Views/MainWindow.axaml` — trophy grid, search/filter bar, status footer
- `ViewModels/MainViewModel.cs` — open, save, export, import, row editing
- `Models/TrophyDto.cs` — the trophy record plus `ExportRootDto`, the export wrapper that carries the system prompt
- `Core/TrophyUtility.cs` — pfdtool decrypt/encrypt calls and temp-folder handling

## Notes

- pfdtool and its key files (`games.conf`, `global.conf`) ship in the output folder, along with `msvcr100.dll`.
- Only mess with trophy folders you own.

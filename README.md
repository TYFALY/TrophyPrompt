# <img width="1280" height="640" alt="TrophyPrompt Banner" src="https://github.com/user-attachments/assets/a7690bca-e116-4ec7-bee1-ca5c52378ecb" />

# 🏆 TrophyPrompt

PS3 trophy timestamp editor for Windows. Open a trophy folder, export it to JSON, let an LLM fill in realistic unlock times, import it back, save. That is the whole program.

Built with Avalonia 11 on .NET 8, ported from [PS3TrophyIsGood](https://github.com/darkautism/PS3TrophyIsGood).

## The workflow

1. **Open Folder** — pick a PS3 trophy directory (something like `NPWR03468_00`). The app decrypts `TROPTRNS.DAT` / `TROPUSR.DAT` with pfdtool.
2. **Export JSON** — writes one file with the game title, title ID, account ID, the full trophy list, and a system prompt footer. The prompt sets the rules: don't rename anything, don't touch existing timestamps, keep everything chronological, platinum pops last.
3. **Paste the JSON into Claude or GPT** and take the timestamps it returns.
4. **Import JSON** — matches entries by trophy ID (falls back to name) and applies the unlocks and times. Old plain-array exports still import fine.
5. **Save** — writes the data back and re-signs the PFD.

<div align="center">
  <img width="800" height="529" alt="TrophyPrompt" src="https://github.com/user-attachments/assets/fb19255d-33ec-4505-9a0f-f5a31ea99e84" />
</div>



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

One catch: a bare clone of this repo does not build on its own. The project file points at sibling folders (`..\TROPHYParser`, `..\BigEndianTool`, `..\PS3TrophyIsGood\pfdtool`) that live next to it in the original checkout. Either clone the full layout or vendor those three in and fix the paths.

## What's inside

- `Views/MainWindow.axaml` — trophy grid, search/filter bar, batch toolbar, animated wave header
- `ViewModels/MainViewModel.cs` — open, save, export, import, row editing
- `Models/TrophyDto.cs` — the trophy record plus `ExportRootDto`, the export wrapper that carries the system prompt
- `Core/TrophyUtility.cs` — pfdtool decrypt/encrypt calls and temp-folder handling

## Notes

- pfdtool and its key files (`games.conf`, `global.conf`) ship in the output folder, along with `msvcr100.dll`.
- Only mess with trophy folders you own. PlayStation is a trademark of Sony; this project is not affiliated with or endorsed by them.

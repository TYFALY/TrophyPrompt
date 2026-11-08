# Third-party notices

TrophyPrompt itself is released under the [MIT License](LICENSE).
It includes or depends on the following third-party components.

| Component | Used for | Author | License |
|-----------|----------|--------|---------|
| [PS3TrophyIsGood](https://github.com/darkautism/PS3TrophyIsGood) (`vendor/TROPHYParser`, `vendor/BigEndianTool`) | Trophy file parsing, big-endian I/O | darkautism | MIT |
| pfdtool (`vendor/pfdtool/pfdtool.exe`, `*.conf`) | PFD decrypt / encrypt / re-sign on Windows | flatz | No license published |
| [pfd_sfo_tools](https://github.com/bucanero/pfd_sfo_tools) (native `pfdtool` in macOS/Linux releases, built from source) | Same as above, on macOS/Linux | flatz, port by bucanero | No license published |
| `msvcr100.dll` | C runtime for `pfdtool.exe` | Microsoft | Visual C++ 2010 Redistributable terms |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | UI framework | AvaloniaUI | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM helpers | .NET Foundation | MIT |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | JSON import/export | James Newton-King | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp), [HarfBuzzSharp](https://github.com/mono/SkiaSharp) | Rendering (via Avalonia) | Microsoft / .NET Foundation | MIT |
| [Inter](https://github.com/rsms/inter) (via `Avalonia.Fonts.Inter`) | UI font | Rasmus Andersson | SIL Open Font License 1.1 |

## About pfdtool

pfdtool has been shared freely in the PS3 scene since 2012, but neither flatz's
original release nor bucanero's port ships a license file. It is included here
with credit to its authors, unmodified. If you are an author and want it
removed or relicensed, please [open an issue](https://github.com/TYFALY/TrophyPrompt/issues).

The MIT License of this repository covers TrophyPrompt's own code only. It does
not relicense any of the components above.

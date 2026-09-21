# Contributing to TrophyPrompt

Thanks for considering a contribution. This is a niche tool and every PR genuinely helps.

## Ways to Contribute

| Area | Why it matters |
|------|----------------|
| **Cross-platform pfdtool** | Biggest gap — decryption/encryption only works on Windows currently |
| **Linux/macOS packaging** | AppImage, DMG, Flatpak, Homebrew formula |
| **Unit tests** | Parser layer (`vendor/TROPHYParser`) has zero tests |
| **UI/UX** | Accessibility, keyboard nav, dark mode polish |
| **Documentation** | More examples, troubleshooting, FAQ |

## Development Setup

```bash
git clone https://github.com/TYFALY/TrophyPrompt.git
cd TrophyPrompt
dotnet restore
dotnet build TrophyPrompt.sln -c Release
dotnet run --project TrophyPrompt.csproj -c Release
```

## Pull Request Checklist

- [ ] `dotnet build` passes (Release config)
- [ ] `dotnet format` — no style warnings
- [ ] Tests pass (when they exist)
- [ ] UI changes include a screenshot
- [ ] Commit messages are clear (conventional commits preferred: `feat:`, `fix:`, `refactor:`, etc.)

## Code Style

- `.editorconfig` at repo root defines formatting rules
- Run `dotnet format` before pushing
- No AI-generated code without human review

## Reporting Issues

Use the issue tracker. Include:
- OS / .NET version
- Steps to reproduce
- Expected vs actual behavior
- Trophy folder structure (sanitized) if relevant

## Questions?

Open a Discussion or issue. No such thing as a dumb question here.
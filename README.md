# SkillCheckSorter

A Dead by Daylight AI training data sorter. Quickly sort raw screenshot images into HIT, MISS, and Unsure folders using keyboard shortcuts.

## Download

Grab the latest installer from [Releases](https://github.com/mesterx07/SkillCheckSorter/releases).

## Features

- Sort images instantly with keyboard shortcuts
- Undo any action including deletions (Ctrl+Z)
- Live session stats — HIT, MISS, UNSURE, DEL counters
- Drag & drop folder support
- Dark UI with configurable folder assignments (0–9)
- Deleted files go to `.trash` and are recoverable until session ends

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `H` / `→` | HIT |
| `M` / `←` | MISS |
| `U` | Unsure |
| `Del` | Delete (recoverable) |
| `Ctrl+Z` | Undo |

## Building from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download) and Windows.

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output will be in `publish/`. The following WPF native DLLs must be distributed alongside the exe:
- `D3DCompiler_47_cor3.dll`
- `PenImc_cor3.dll`
- `PresentationNative_cor3.dll`
- `vcruntime140_cor3.dll`
- `wpfgfx_cor3.dll`

## License

MIT

# EZManifest (Linux)

<p align="center">
  <img src="EZManifest/Assets/EZManifestLogo.png" alt="EZManifest" width="360" />
</p>

Linux desktop app for importing Steam depot manifests, downloading game files from Steam CDN, and managing a local library.

This is the Linux port of [EZManifest](https://github.com/dpadGuy/EZManifest), packaged as a self-contained AppImage. The Windows version (WinUI 3) lives in the original repository.

## Features

- **Library** — browse installed/imported games with cover art
- **Downloads** — import a manifest `.zip`, pick depots, download with pause/cancel
- **CDN region** — choose a Steam content cell (or Auto) in Settings
- **Install path** — set a default download/install root
- **Play** — launch a saved executable with the game folder as working directory
- **Context menu** — open install folder, uninstall
- **Theme** — light / dark
- **Proton** — Windows executables launch through Steam Proton or GE-Proton (official builds and `compatibilitytools.d` are discovered automatically); native Linux launchers (`run.sh`, `start.sh`, `run`) launch directly
- **Wine prefixes** — per-game or app-wide `compatdata` prefix, configurable in Settings
- **Environment overrides** — `KEY=VALUE` pairs (e.g. `PROTON_LOG=1`, `DXVK_HUD=1`) for Proton and native launches, global or per-game
- **Shortcuts** — `.desktop` shortcuts and Steam non-Steam shortcuts (all local accounts)

## Technologies

- [.NET 8](https://dotnet.microsoft.com/) (`net8.0`)
- [Avalonia](https://avaloniaui.net/) 11.2 + [WebViewControl-Avalonia](https://github.com/tomviz/WebViewControl) (CEF)
- [SteamKit2](https://github.com/SteamRE/SteamKit) 3.4 — depot manifests & CDN chunk processing
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm) 8.4
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) 9.0
- C# / XAML (self-contained AppImage)

## Requirements

- x86_64 Linux with a desktop environment (X11)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build
- Steam with at least one Proton build (official Proton or GE-Proton) to launch Windows executables
- Common CEF/WebView runtime libraries (`libx11`, `libnss3`, `libasound2`, …) — the CI workflow lists the full set

## Build

```bash
dotnet build EZManifest.Linux/EZManifest.Linux.csproj -c Debug
```

Or run it directly:

```bash
dotnet run --project EZManifest.Linux/EZManifest.Linux.csproj
```

## Package the AppImage

```bash
EZManifest.Linux/Packaging/publish-linux.sh
EZManifest.Linux/Packaging/build-appimage.sh 1.2.2
```

`publish-linux.sh` publishes self-contained and stages SteamAutoCrack.CLI next to the app binary. `build-appimage.sh` packages `publish/linux-x64` into `publish/out/EZManifest-<version>-x86_64.AppImage` with [linuxdeploy](https://github.com/linuxdeploy/linuxdeploy) (put `linuxdeploy-x86_64.AppImage` in `EZManifest.Linux/publish/tools` or point `LINUXDEPLOY` at it).

CI (`.github/workflows/build-linux.yml`) does both on every push/PR and uploads the AppImage as an artifact.

## Runtime data

Runtime data lives in `~/.local/share/EZManifest`:

| Path | Purpose |
|------|---------|
| `settings.json` | Download path + CDN cell |
| `items.json` | Library entries |
| `Manifests/` | Extracted manifest archives |
| `game-launch.log` | Detached game launch trace |
| `WebViewCache/` | Embedded WebView cache |

## Usage

1. Set an install location in **Settings** (prompted on first launch if missing).
2. Open **Downloads**, browse to a manifest `.zip`.
3. Select depots that have local `depotId_manifestId.manifest` files and a matching key in the `.lua`.
4. Download; the game appears in **Library**.
5. **Play** picks an `.exe` the first time and remembers it. Enable **Play with Proton** in Settings and pick a Proton version (or Auto) for Windows executables; native Linux games launch directly.

Depot list is driven by **on-disk `.manifest` files** and keys from `addappid(...)` in the lua — `setManifestid(...)` is ignored.

## Project layout

```
EZManifest-Linux/
  EZManifest.slnx
  EZManifest.Linux/         Linux app (Avalonia)
    EZManifest.Linux.csproj
    Views/                   Library, Downloads, DepotBox, Patch, Settings
    ViewModels/
    Services/               Proton launching, shortcuts, dialogs
    Shims/                   WinUI type stubs shared ViewModels compile against
    Packaging/               publish-linux.sh, build-appimage.sh
  .github/workflows/         Linux AppImage CI
```

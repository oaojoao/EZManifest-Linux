# EZManifest

<p align="center">
  <img src="EZManifest/Assets/EZManifestLogo.png" alt="EZManifest" width="360" />
</p>

Windows and Linux desktop app for importing Steam depot manifests, downloading game files from Steam CDN, and managing a local library.

## Features

- **Library** — browse installed/imported games with cover art
- **Downloads** — import a manifest `.zip`, pick depots, download with pause/cancel
- **CDN region** — choose a Steam content cell (or Auto) in Settings
- **Install path** — set a default download/install root
- **Play** — launch a saved executable with the game folder as working directory
- **Context menu** — open install folder, uninstall
- **Theme** — light / dark

Linux extras (Avalonia build):

- **Proton** — Windows executables launch through Steam Proton or GE-Proton (official builds and `compatibilitytools.d` are discovered automatically); native Linux launchers (`run.sh`, `start.sh`, `run`) launch directly
- **Wine prefixes** — per-game or app-wide `compatdata` prefix, configurable in Settings
- **Environment overrides** — `KEY=VALUE` pairs (e.g. `PROTON_LOG=1`, `DXVK_HUD=1`) for Proton and native launches, global or per-game
- **Shortcuts** — `.desktop` shortcuts and Steam non-Steam shortcuts (all local accounts)
- **Post-download step** — SteamAutoCrack.CLI runs through Proton when staged next to the app

## Technologies

- [.NET 8](https://dotnet.microsoft.com/) (`net8.0-windows10.0.19041.0` on Windows, `net8.0` on Linux)
- **Windows**: [WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/) / [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) 2.4
- **Linux**: [Avalonia](https://avaloniaui.net/) 11.2 + [WebViewControl-Avalonia](https://github.com/tomviz/WebViewControl) (CEF)
- [SteamKit2](https://github.com/SteamRE/SteamKit) 3.4 — depot manifests & CDN chunk processing
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm) 8.4
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) 9.0
- C# / XAML (unpackaged Win32 desktop app on Windows, self-contained AppImage on Linux)

## Requirements

Windows:

- Windows 10 version 1903+ (build 18362+) recommended; targets `net8.0-windows10.0.19041.0`
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build
- Visual Studio 2022 with **Windows application development** workload (WinUI) recommended

Linux:

- x86_64 Linux with a desktop environment (X11)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build
- Steam with at least one Proton build (official Proton or GE-Proton) to launch Windows executables
- Common CEF/WebView runtime libraries (`libx11`, `libnss3`, `libasound2`, …) — the CI workflow lists the full set

## Build

Windows:

```bash
dotnet build EZManifest\EZManifest.csproj -c Debug -p:Platform=x64
```

Linux:

```bash
dotnet build EZManifest.Linux/EZManifest.Linux.csproj -c Debug
```

Open `EZManifest.slnx` in Visual Studio and run (x64), or `dotnet run --project EZManifest.Linux/EZManifest.Linux.csproj` on Linux.

## Publish

Windows, from the repo root:

```bat
publish.bat
```

Or:

```bash
dotnet publish EZManifest\EZManifest.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -o publish
```

Output:

- `publish\EZManifest.exe` (self-contained single-file)
- `publish\SteamAutoCrack.CLI\` (downloaded from the v3.5.0.7 release)
- `installer\EZManifest-Setup-1.2.2.exe` (Inno Setup, if ISCC is installed)

The installer does not need admin. It defaults to `%LocalAppData%\Programs\EZManifest` (you can pick another folder) and leaves `%LocalAppData%\EZManifest` alone on uninstall.

Linux AppImage:

```bash
EZManifest.Linux/Packaging/publish-linux.sh
EZManifest.Linux/Packaging/build-appimage.sh 1.2.2
```

`publish-linux.sh` publishes self-contained and stages SteamAutoCrack.CLI next to the app binary. `build-appimage.sh` packages `publish/linux-x64` into `publish/out/EZManifest-<version>-x86_64.AppImage` with [linuxdeploy](https://github.com/linuxdeploy/linuxdeploy) (put `linuxdeploy-x86_64.AppImage` in `EZManifest.Linux/publish/tools` or point `LINUXDEPLOY` at it).

CI (`.github/workflows/build-linux.yml`) does both on every push/PR and uploads the AppImage as an artifact.

## Runtime data

Windows: `%LocalAppData%\EZManifest` — Linux: `~/.local/share/EZManifest`

| Path | Purpose |
|------|--------|
| `settings.json` | Download path + CDN cell |
| `items.json` | Library entries |
| `Manifests\` | Extracted manifest archives |
| `game-launch.log` (Linux) | Detached game launch trace |
| `WebViewCache\` (Linux) | Embedded WebView cache |

## Usage

1. Set an install location in **Settings** (prompted on first launch if missing).
2. Open **Downloads**, browse to a manifest `.zip`.
3. Select depots that have local `depotId_manifestId.manifest` files and a matching key in the `.lua`.
4. Download; the game appears in **Library**.
5. **Play** picks an `.exe` the first time and remembers it. On Linux, enable **Play with Proton** in Settings and pick a Proton version (or Auto) for Windows executables; native Linux games launch directly.

Depot list is driven by **on-disk `.manifest` files** and keys from `addappid(...)` in the lua — `setManifestid(...)` is ignored.

## Project layout

```
EZManifest/
  EZManifest.slnx
  publish.bat
  installer.iss
  EZManifest/               Windows app (WinUI 3)
    EZManifest.csproj
    Views/Pages/             Library, Downloads, Patch, Settings
    Services/               Download engine, Steam metadata, settings, …
    Models/
  EZManifest.Linux/         Linux app (Avalonia)
    EZManifest.Linux.csproj
    Views/                   Library, Downloads, DepotBox, Patch, Settings
    ViewModels/
    Services/               Proton launching, shortcuts, dialogs
    Shims/                   WinUI type stubs shared ViewModels compile against
    Packaging/               publish-linux.sh, build-appimage.sh
  .github/workflows/         Windows build + Linux AppImage CI
```

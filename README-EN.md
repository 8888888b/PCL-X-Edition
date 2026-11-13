[简体中文](README.md) | **English** | [繁體中文](README-ZH_TW.md)

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.png" alt="Logo" width="80" height="80">

</div>

# PCL XE

> **PCL XE (Plain Craft Launcher XE, eXtended Edition)** is an experimental fork of [PCL Symbio Edition](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition). It adds two top-level pages — **China Edition** and **Ask AI** — while mod management stays where it always was (Version Settings → Mods).

## What PCL XE changes

| Feature | Description |
| --- | --- |
| China Edition | Locates the Minecraft China Edition `.minecraft` folder (MCLDownload / netease markers / netease fields in version JSON) or lets you pick it manually, registers it as a game folder and **launches its versions through the Java Edition pipeline** instead of starting the official launcher. International `.minecraft` folders are never mistaken for it. |
| Mod management | Back in its original place: Instance → Version Settings → Mods, with the upstream enable/disable, `.disabled` suffix and metadata logic, no longer taking a separate tab. |
| Ask AI | Talk to the launcher: configure any OpenAI-compatible `/chat/completions` endpoint, ask "install JEI for me", and the launcher parses the JSON actions and runs them against an allowlist. |
| Branding | Icon, window title and UI text are all XE; no "SE" naming is left. |

### Limits of China Edition launching

- Only reads an existing local China Edition `.minecraft`; it does not download, crack or read NetEase accounts. Account verification, version downloads and component updates remain the official client's job.
- Launches reuse the PCL Java pipeline (offline account, automatic Java selection, assets/libraries pointed at that `.minecraft`), so the version JSON must be complete. Heavily customized versions that require a NetEase login may behave differently from the official launcher.
- The first launch writes that `.minecraft` into the game folder list so you can switch to it from the launch page.
- Automatic detection only accepts folders that are provably China Edition: path contains `MCLDownload` / `netease`, netease files exist inside, or the version JSON carries netease fields. International `.minecraft` folders have the same structure, so they are not listed; picking one manually shows a warning.

## Features

### Java Edition

- One-click installation of official releases, snapshots and historical versions, with automatic dependency completion and Java runtime selection
- 8 installation options: Forge, NeoForge, Fabric, Legacy Fabric, OptiFine, LiteLoader, Cleanroom, LabyMod
- Modpack installation and mod loader switching

### Bedrock Edition

- Detects the Minecraft Bedrock editions installed on your system, distinguishing legacy UWP builds from GDK builds
- Imports installation packages and unpacks them into a separate directory without affecting other versions
- Built-in Xbox account sign-in, so you can launch right after logging in
- GDK portable builds can pick a per-version loader: Vanilla, LeviLamina (auto-installed for the matching game version) or BLoader (bundled with the launcher)
- Filter by version type, search by version number and manage local installs

### Resource downloads

- Dual-source search across CurseForge and Modrinth
- Covers mods, modpacks, shaders, resource packs, datapacks and worlds, placing downloads into the correct directories automatically

### Instance management

- Each instance keeps its own mods, saves, screenshots and configuration
- Disable individual mods at any time to troubleshoot conflicts
- Browse and back up saves and screenshots, and open their folders in one click
- Keep a list of frequently used servers per instance and export the whole instance as an archive

### User experience

- Complete interface resources for Simplified Chinese and English (US); Traditional Chinese, English (UK), Japanese, French and Spanish currently contain only language metadata, with interface text falling back to Simplified Chinese
- Customizable color theme and background, with transition animations for page switching and list scrolling
- Automatic Java scanning to reduce manual configuration
- Log analysis on failed launches, with readable causes and suggestions
- Running memory is returned to the system when the launcher is idle or in the background, so it does not grow the longer it stays open

> PCL XE does not include the multiplayer lobby; the related modules have been removed from the codebase, and there is no confirmed schedule for restoring them.

## Supported platforms

| Operating System | Support Status | Requirements |
|---|---|---|
| Windows 10 1809 (17763) or later | Fully supported | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 8 to Windows 10 1809 (17763) | Expected to run; support offered at discretion | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 7 or earlier | Not supported | N/A |
| macOS / Linux / Other OS | Cross-compilation only; cannot run directly | [.NET 10 SDK](https://get.dot.net/10) |

**Fully supported**: We will provide as much support as possible, but you must ensure you are using the latest version of the launcher.

**Expected to run; support offered at discretion**: PCL XE is expected to run on these platforms, but full functionality is not guaranteed. You may need to upgrade to a fully supported OS version to receive further technical support.

**Not supported**: PCL XE may not be usable on these platforms and might not even launch at all. Please upgrade your OS to use PCL XE.

**Cross-compilation only**: The source code can be built on macOS and Linux but cannot run there directly. As a developer, you can develop on these platforms and move the build artifacts to Windows for testing.

**Note**: Only the latest version of the launcher is supported. Depending on the nature of certain issues (e.g., an incomplete system environment), you may still need to upgrade your OS to continue receiving support. You can still try using PCL XE on unsupported platforms, but you may encounter many additional problems.

## Download and usage

1. Download the latest release from [Releases](https://github.com/8888888b/PCL-X-Edition/releases)
2. Extract it to any directory (the launcher is portable and does not write to system directories)
3. Run `Plain Craft Launcher XE.exe`, pick Java Edition, Bedrock Edition, or open **China Edition** / **Ask AI**; mods live in Instance → Version Settings → Mods

> PCL XE has its own version number and points its updater at this repository's releases, so an upstream PCL Symbio Edition release cannot overwrite it. You can also switch to a self-hosted manifest in Settings → About & Update.

Official release builds are framework-dependent and require the [.NET 10 Desktop Runtime](https://get.dot.net/10); self-contained builds you compile yourself do not.

## Building from source

Requires the [.NET 10 SDK](https://get.dot.net/10). [global.json](global.json) selects SDK `10.0.403` and allows patch updates within the same feature band; CI installs that baseline version. Run the following in the repository root:

```powershell
# Framework-dependent publish, matching CI
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true `
  -p:DeleteExistingFiles=true -o ./artifact --no-self-contained

# Self-contained single-file publish (bundles the runtime, much larger output)
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true --self-contained true -o ./publish
```

Replace `-p:Platform=x64` with `-p:Platform=ARM64` to build for ARM64.

Available configurations (`-c`): `Release`, `Beta`, `Debug`, `CI`. Available platforms (`-p:Platform`): `x64`, `ARM64`, `AnyCPU`.

See the [contribution guide](CONTRIBUTING.md#本地构建与测试) for test commands, the offline test scope and release configuration. The solution does not build the core test project by default; run that project explicitly.

## Project structure

| Directory | Description |
|---|---|
| `Plain Craft Launcher 2/` | WPF application: UI, pages and Minecraft launching logic |
| `PCL.Core/` | Core library: configuration, networking and downloads, Java, saves, animation, localization, logging |
| `PCL.Core.SourceGenerators/` | Source generators for configuration and dependency injection |
| `PCL.Core.Test/` | Unit tests for PCL.Core |
| `.github/` | CI build and release workflows |

## Differences from upstream

- New **China Edition** page: locate and launch Minecraft China Edition versions without starting the official launcher
- New **Ask AI** page: connect any OpenAI-compatible endpoint and let the launcher run allowlisted actions from a conversation
- Mod management back in Version Settings → Mods instead of a separate tab
- Its own update source and independent version number (`2.14.04`)
- Removed the multiplayer lobby and its modules; no lobby or multiplayer entry points are provided

## Legal notice

> These notices are also shown in the "About this version" dialog after the launcher updates.

- **Project nature**: PCL XE (Plain Craft Launcher XE) is a third-party experimental fork of PCL Symbio Edition, maintained by an individual developer. It is not affiliated with the official PCL2 project maintained by LTCatt (龙腾猫跃), nor with the official PCL CE project maintained by PCL-Community.
- **Scope**: the China Edition page only uses a locally installed China Edition client folder and provides no cracking or access to private data; Java and Bedrock features come from upstream.
- **APIs**: PCL XE accesses Minecraft international services only through APIs approved by Mojang Studios.
- **Code origin**: This project contains no Minecraft code (including Minecraft China Edition) and no code from Mojang Studios or its agents in China.
- **Name usage**: The word "Minecraft" appears in documentation and demonstrations solely to describe the game this project supports, which constitutes fair use under the Copyright Law of the People's Republic of China. Copyright in related screenshots and videos belongs to their respective developers.
- **Relationships**: PCL XE has no affiliation, partnership, maintenance or licensing relationship with PCL2, PCL CE, PCL Symbio Edition, Mojang Studios or its agents in China.
- **Genuine copies**: We encourage buying and playing genuine copies of Minecraft through official channels.
- **Multiplayer lobby**: PCL XE does not provide the PCL CE multiplayer lobby. The related modules have been removed, and there is no confirmed schedule for restoring them.

## License

- `Plain Craft Launcher 2/` uses a [custom license](https://github.com/8888888b/PCL-X-Edition/blob/main/Plain%20Craft%20Launcher%202/LICENCE)
- All other directories use [Apache License 2.0](https://github.com/8888888b/PCL-X-Edition/blob/main/LICENSE)
- Independently added code in this project (China Edition, Ask AI, auto update) is licensed under GNU GPLv3; see [LICENSE-GPL-3.0](./LICENSE-GPL-3.0)

> Important: only code independently added by this project is bound by GPLv3; upstream code keeps its original license.
> Minecraft and its trademarks and game assets belong to Mojang/Microsoft.

## Acknowledgements

- [PCL](https://github.com/Meloong-Git/PCL) — original author 龙腾猫跃 (LTCat)
- [PCL Symbio Edition](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition) — direct upstream of this fork, source of the Bedrock Edition support
- [BedrockBoot](https://github.com/Round-Studio/BedrockBoot) and [BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core) — Bedrock projects by Round Studio (GPL-3.0), porting reference for this project's Bedrock installation, decryption and launching; see [NOTICE](NOTICE) for scope
- [BLoader](https://github.com/Chlna6666/BLoader) — Bedrock mod loader by Chlna6666 (GPL-3.0), bundled and injected with the launcher; see [THIRD-PARTY/BLoader/LICENSE](<Plain Craft Launcher 2/THIRD-PARTY/BLoader/LICENSE>)
- [LeviLamina](https://github.com/LiteLDev/LeviLamina) and [lip](https://github.com/LiteLDev/lip) — LeviLamina client installed at runtime through the official lip for the matching game version (LGPL-3.0 / GPL-3.0), not redistributed with this project
- Plus open-source projects such as Dapper, Downloader, Markdig.Wpf, NAudio, Microsoft.Data.Sqlite, protobuf-net and Lucide. See [metadata.json](Plain%20Craft%20Launcher%202/metadata.json) and the launcher's "About" page for the complete list of third-party components and licenses

## Statistics

**Views** (Total / Today): [![Hits](https://hits.zkitefly.eu.org/?tag=https://github.com/8888888b/PCL-X-Edition)](https://hits.zkitefly.eu.org/?tag=https://github.com/8888888b/PCL-X-Edition&web=true)

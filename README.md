# DV Mod Manager

Desktop mod manager for Derail Valley, built with Avalonia UI and .NET 8.

DV Mod Manager scans your `Mods` and `Mods.inactive` folders, lets you quickly activate/deactivate mods, and provides profile, update, backup, and rollback workflows.

## Highlights

- Cross-platform desktop app (Avalonia) targeting .NET 8.
- Auto-detects Derail Valley install path (Steam on Windows and Linux).
- Built-in Unity Mod Manager (UMM) loader install.
- Scans and watches `Mods` and `Mods.inactive` for changes.
- Install mods from `.zip` archives (expects `Info.json`).
- Create and manage custom mod groups in the Available and Active lists.
- One-click activate/deactivate for single mods or grouped mods.
- Multi-select mods with checkboxes or CTRL+click; activate/deactivate all checked mods at once.
- Click a group header to select all mods in that group; CTRL+click to append/toggle.
- Drag one or multiple selected mods between groups.
- Automatically activates inactive required dependencies when enabling a mod (when available locally).
- Update checks via:
  - GitHub releases (if defined in the `Info.json`).
  - Nexus Mods API (when API key is configured).
- Bulk update for downloadable GitHub updates.
- Version archive cache with rollback support.
- Profile system to save/apply mod states.
- Import/export profiles as JSON or as self-contained ZIP modpacks.
- Optional automatic backup before bulk changes.
- Safety lock while Derail Valley is running.

# [Checkout the Wiki for up to date Information](https://github.com/Fuggschen/DVModManager/wiki)

## Supported Mod Metadata

This app reads Unity Mod Manager style `Info.json` metadata.

Important fields include:

- `Id`
- `DisplayName`
- `Version`
- `Author`
- `Requirements`
- `Repository`
- `HomePage`

If `Info.json` is missing or invalid, the mod folder can still be shown, but with limited metadata.

## Requirements

- .NET SDK 8.0+
- Derail Valley installed (Steam)
- Windows or Linux

## Getting Started

For most users, setup is just downloading a release build:

1. Open the GitHub Releases page for this repository.
2. Download the zip for your OS (`win-x64` or `linux-x64`).
3. Extract and run `DVModManager`.

On first launch, the app will:

1. Load settings from your user profile.
2. Try to auto-detect the game path.
3. Ask for a game folder if detection fails.

## App Data Locations

Settings and runtime data are stored under:

- `%APPDATA%/DVModManager` on Windows
- `$HOME/.config`-based locations depending on platform runtime behavior

Key paths used by the app:

- `settings.json`
- `logs/`
- `downloads/`
- `versions/`
- `profiles/`
- `backups/`

## How Mod State Works

- Active mods live in `<GamePath>/Mods`
- Inactive mods live in `<GamePath>/Mods.inactive`
- Activating/deactivating a mod moves its folder between these directories.

For updates and rollbacks, current versions are archived to the local version cache before replacement/removal when applicable.

## Mod Loader (UMM)

Derail Valley mods run on the [Unity Mod Manager](https://github.com/newman55/unity-mod-manager)
loader. DV Mod Manager can install, update and remove that loader for you.
There is no need to run UMM itself:

- Settings → *Mod Loader* shows the loader state (installed, version, install
  method) and offers **Install/Update** and **Uninstall** buttons. On startup,
  if the game has no loader yet, the app prompts to install it.
- The loader package is downloaded on demand from UMM's official update feed
  and cached locally (`downloads/umm/`), so nothing UMM-related ships inside
  this app and reinstalls work offline once cached.
- **Injection method** (Settings → Mod Loader):
  - *Doorstop*: `winhttp.dll` + `doorstop_config.ini` in the game folder
    (default of the Windows build).
  - *Assembly*: IL-patches `UnityEngine.CoreModule.dll` with UMM's starter
    (default of the Linux build; a backup is kept and
    restored on uninstall).
- **Updates**: the settings show the installed and latest loader version. The install button updates to the newest UMM when needed.

## Profiles

Profiles store desired mod active/inactive state and version targets.

You can:

- Save current setup as a profile
- Apply a saved profile
- Import/export profiles as JSON
- Export a profile as a self-contained ZIP modpack (bundles all mod files alongside the profile)
- Import a modpack ZIP via the **Install** button — the app shows the full mod list and a security warning before importing; profile names are deduplicated automatically
- Delete profiles you no longer need

Applying a profile can trigger:

- Activations/deactivations
- Version rollbacks (if required and available)
- Download missing mods (if Repository is defined in `Info.json`)

> **Redistribution note:** When exporting a ZIP modpack the app reminds you that most mods do not permit redistribution on third-party sites. Share modpack ZIPs only within the limits of each mod's licence.

> **Security note:** ZIP modpacks contain the actual mod binaries. Only import modpacks from sources you trust.

### In-game profile associations

The optional [DVModProfiles](DVModProfiles/README.md) mod adds a "Mod Profile" row to Derail Valley's
save menu, listing the profiles this app has saved under `profiles/` and letting you tag each save
with the one it belongs to. When you load a save whose profile isn't the one the app last applied,
the mod warns you and offers to quit so you can switch profiles in the app to the correct one.

The in-game menu also allows you to optionally associate UMM mod settings with a profile, so that the correct
settings for a mod will be applied when it is loaded.

### Steam Cloud sync

With the mod installed and its Steam Cloud setting on, your `profiles/` files are mirrored into
Derail Valley's cloud quota and merged onto your other machines. Steam synchronizes the files whenever the 
the game starts or quits.

## Updates

- `Check Updates` scans all detected mods.
- GitHub updates with direct downloadable assets can be installed in-app.
- Nexus updates (and non-direct-download cases) open the mod page for manual download/install.

## Logging and Error Handling

- File logs are written to the app logs directory.
- Unhandled exceptions are captured and logged.
- The UI status bar reports operational feedback.

## Repository Structure

This repository contains both a desktop app (DVModManager) and an in-game supporting mod (DVModProfiles).

```text
DVModManager.slnx
DVModManager/
  Models/
  Services/
  ViewModels/
  Views/
  Converters/
  Assets/
DVModProfiles/
  DVModProfiles.slnx
  DVModProfiles.csproj
  Profiles/
  UI/
  info.json
  repository.json
  package.ps1
  deploy-debug.ps1
```

The two are deliberately kept in separate solutions. `DVModProfiles`
compiles against Derail Valley's shipped assemblies, so it cannot be built without
the game installed. See [DVModProfiles/README.md](DVModProfiles/README.md) for the
mod's reference setup, packaging, and release process.

## Contributing

Check the [Wiki Pages](https://github.com/Fuggschen/DVModManager/wiki/Contributing) for more information on contributing

## Credits
- [UnityModManager](https://github.com/newman55/unity-mod-manager) - For the Inspiration and foundation (MIT). The loader installed by this app is UMM's own work, downloaded from its official feeds and not redistributed with DV Mod Manager.
- [UnityDoorstop](https://github.com/NeighTools/UnityDoorstop) - Doorstop proxy used by UMM's Doorstop install method (LGPL-2.1, distributed as part of UMM's packages)
- [SharkBaitDLS](https://github.com/SharkBaitDLS) - For helping me with all kinds of things and DVModProfiles
- [Syraphya](https://vgen.co/Syraphya) - For the Application Logo
### Contributors
- [SharkBaitDLS](https://github.com/SharkBaitDLS)
- [red_rass](https://github.com/rassiMC)

## License

MIT - see [LICENSE](LICENSE)

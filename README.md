# DV Mod Manager

Desktop mod manager for Derail Valley, built with Avalonia UI and .NET 8.

DV Mod Manager scans your `Mods` and `Mods.inactive` folders, lets you quickly activate/deactivate mods, and provides profile, update, backup, and rollback workflows.

## Highlights

- Cross-platform desktop app (Avalonia) targeting .NET 8.
- Auto-detects Derail Valley install path (Steam on Windows and Linux).
- Scans and watches `Mods` and `Mods.inactive` for changes.
- Install mods from `.zip` archives (expects `Info.json`).
- Create and manage custom mod groups in the Available and Active lists.
- One-click activate/deactivate for single mods or grouped mods.
- Automatically activates inactive required dependencies when enabling a mod (when available locally).
- Update checks via:
  - GitHub releases (if defined in the `Info.json`).
  - Nexus Mods API (when API key is configured).
- Bulk update for downloadable GitHub updates.
- Version archive cache with rollback support.
- Profile system to save/apply mod states.
- Import/export profiles as JSON.
- Optional automatic backup before bulk changes.
- Safety lock while Derail Valley is running.

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

## Profiles

Profiles store desired mod active/inactive state and version targets.

You can:

- Save current setup as a profile
- Apply a saved profile
- Import/export profiles as JSON
- Delete profiles you no longer need

Applying a profile can trigger:

- Activations/deactivations
- Version rollbacks (if required and available)

## Updates

- `Check Updates` scans all detected mods.
- GitHub updates with direct downloadable assets can be installed in-app.
- Nexus updates (and non-direct-download cases) open the mod page for manual download/install.

Optional credentials in Settings:

- GitHub token (to improve API rate limits)
- Nexus API key

## Logging and Error Handling

- File logs are written to the app logs directory.
- Unhandled exceptions are captured and logged.
- The UI status bar reports operational feedback.

## Repository Structure

```text
DVModManager.slnx
DVModManager/
  Models/
  Services/
  ViewModels/
  Views/
  Converters/
  Assets/
```

## Contributing

Issues and pull requests are welcome.

### Branching and PR policy

- Please branch from `dev` and open PRs targeting `dev`.
- The `dev` branch is the most up-to-date integration branch.
- PRs should only be merged after the `dev` branch build pipeline is passing.

### Developer setup

Requirements:

- .NET SDK 8.0+
- Derail Valley install for local testing

Build and run from repository root:

```bash
dotnet restore
dotnet build DVModManager/DVModManager.csproj
dotnet run --project DVModManager/DVModManager.csproj
```

### Publishing (for maintainers/contributors)

The project is configured for self-contained single-file publish.

CI/CD release automation:

- The release workflow runs on pushed tags matching `v*`.
- Tags created from `main` or `beta` will trigger automatic release packaging and GitHub Release creation.
- If the tag name contains `-beta`, the created GitHub Release is marked as draft/prerelease.

Windows x64:

```bash
dotnet publish DVModManager/DVModManager.csproj -c Release -r win-x64
```

Linux x64:

```bash
dotnet publish DVModManager/DVModManager.csproj -c Release -r linux-x64
```

### Contribution notes

- Keep behavior safe around file operations.
- Preserve rollback/backup guarantees.
- Test with both active and inactive mod layouts.

## License

MIT - see [LICENSE](LICENSE)

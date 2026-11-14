## Description

This is a supplementary mod for Derail Valley to associate mod profiles with save files and to
provide Steam Cloud synchronization of said associations and the configured mod settings.

### Multiplayer

With the [Multiplayer mod](https://github.com/AMacro/dv-multiplayer) installed, a host running
this mod sends its current mod settings to each player who joins. Clients will get a prompt the first
time they join a session with this feature enabled:

- **Always**: play with the host's settings every time they join that server.
- **Once**: keep a copy of the host's settings from that join, and play with the copy from then
  on without syncing again.
- **Never**: keep playing with their own settings.

A player's own settings are restored to their previous state when they leave the game.

Hosts can prevent sharing of their settings with a toggle in this mod's settings, or keep only some
mods' settings to themselves by unticking those mods under it. The Multiplayer mod's own settings
are never shared since they contain server credentials and other potential secrets.

## Building

Building the project requires some initial setup, after which running `dotnet build`
will do a Debug build or running `dotnet build -c Release` will do a Release build.
Run these from this `DVModProfiles` directory, or point at the project explicitly from
the repository root:

```
dotnet build DVModProfiles/DVModProfiles.csproj -c Release
```

Note that the mod is not part of `DVModManager.slnx`. Use `DVModProfiles/DVModProfiles.slnx`
to open the mod on its own.

### References Setup

Some setup is required in order to successfully build the mod DLLs. You will need to
create a new [Directory.Build.targets][references-url] file to specify your local
reference path. This file goes in this `DVModProfiles` directory, next to
`DVModProfiles.csproj`. It is gitignored, as the path is specific to your machine.

Below is an example of the necessary structure. When creating your targets file, you
will need to replace the reference path with the corresponding folder on your system.
Note that any shortcuts you might use in file explorer—such as %ProgramFiles%—won't be
expanded in these paths. You have to use full, absolute paths.
```xml
<Project>
	<PropertyGroup>
		<ReferencePath>C:\Program Files (x86)\Steam\steamapps\common\Derail Valley\DerailValley_Data\Managed\</ReferencePath>
	</PropertyGroup>
</Project>
```

`ReferencePath` is a semicolon-separated list of directories to search, not a single
path, and the project names no per-assembly paths of its own. A normal local setup only
needs the one `Managed` folder shown above, since the game ships its Unity assemblies
there alongside its own. Where the references are split across several folders — as they
are on the CI runner — list each one:

```
C:\References\Derail Valley;C:\References\Unity
```

CI sets this as a `ReferencePath` environment variable instead of a targets file. A build
with `ReferencePath` unset fails with an explicit message saying so.

## Packaging

To package a build for distribution, you can run the `package.ps1` PowerShell script in
this directory. If no parameters are supplied, it will create a .zip file ready for
distribution in the `dist` directory. A post build event is configured to run this
automatically after each successful Release build.

Linux: `pwsh ./package.ps1`
Windows: `powershell -executionpolicy bypass .\package.ps1`

### Parameters

Some parameters are available for the packaging script.

#### -NoArchive

Leave the package contents uncompressed in the output directory.

#### -OutputDirectory

Specify a different output directory.
For instance, this can be used in conjunction with `-NoArchive` to copy the mod files
into your Derail Valley installation directory.

## Deploying a debug build

`deploy-debug.ps1` builds in Debug and copies the DLLs and `info.json` straight into your
game's `Mods/DVModProfiles` folder, reading the game location from your
`Directory.Build.targets`. The mod must already be installed once for the target folder
to exist.

## Testing multiplayer on one PC

`run-second-instance.ps1` starts a second copy of the game next to one that's already running, so
you can join your own multiplayer game with the same Steam account. Unity Mod Manager loads mods
from `Mods` next to the game, so the second copy runs from a game folder of its own
(`%LOCALAPPDATA%\DVModProfiles\SecondInstance` by default). That folder holds copies of the game's
executables, junctions to its `DerailValley_Data` and `MonoBleedingEdge` folders, and its own copy
of `Mods`, which gives it its own mod settings for the host's to replace. To test, start the first
copy and host a game from it, run the script, then direct connect to `127.0.0.1` from the second
copy. Don't delete the instance folder recursively, as that would follow the junctions into the
game's install.

The first run copies the game's whole `Mods` folder. Later runs copy over only the mods' DLLs and
`info.json` files, along with any mods new to the main copy, so a fresh `deploy-debug.ps1` reaches
both copies while the second copy keeps its own settings. `-Reset` copies everything again. The
second copy also gets:

- its own player in multiplayer games: a new Multiplayer GUID, and the name from `-Username`
  (`Player2` by default)
- Steam Cloud turned off in this mod, so the two copies don't sync into each other
- no remembered host choices to start with, so you get the prompt when you join

It runs windowed and out of VR, with its log in its own folder. Both copies still share the game's
saves, preferences and Unity Mod Manager's own files.

## License

Source code is distributed under the MIT license.
See [LICENSE][license-url] for more information. Note that this mod carries its own
copyright, separate from the mod manager's.

[license-url]: https://github.com/Fuggschen/DVModManager/blob/main/DVModProfiles/LICENSE
[references-url]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022
[repo-url]: https://github.com/Fuggschen/DVModManager

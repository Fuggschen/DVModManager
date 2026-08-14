## Description

This is a supplementary mod for Derail Valley to associate mod profiles with save files and to
provide Steam Cloud synchronization of said associations and the configured mod settings.

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

`deploy-debug.ps1` builds in Debug and copies the DLL and `info.json` straight into your
game's `Mods/DVModProfiles` folder, reading the game location from your
`Directory.Build.targets`. The mod must already be installed once for the target folder
to exist.

## License

Source code is distributed under the MIT license.
See [LICENSE][license-url] for more information. Note that this mod carries its own
copyright, separate from the mod manager's.

[license-url]: https://github.com/Fuggschen/DVModManager/blob/main/DVModProfiles/LICENSE
[references-url]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022
[repo-url]: https://github.com/Fuggschen/DVModManager

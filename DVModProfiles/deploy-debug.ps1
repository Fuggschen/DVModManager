Set-Location "$PSScriptRoot"

$modInfo = Get-Content -Raw -Path "info.json" | ConvertFrom-Json
$modId = $modInfo.Id

$ReferencePath = ([xml](Get-Content -Raw -Path "Directory.Build.targets")).Project.PropertyGroup.ReferencePath.Trim()
if (!$ReferencePath) { throw "Could not read ReferencePath from Directory.Build.targets." }
$GamePath = Split-Path -Parent (Split-Path -Parent $ReferencePath)

$ModInstallDir = Join-Path $GamePath "Mods/$modId"
if (!(Test-Path $ModInstallDir)) {
	throw "Mod install dir not found: $ModInstallDir. Install the mod once (or check ReferencePath in Directory.Build.targets)."
}

dotnet build -c Debug "DVModProfiles.csproj"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Copy-Item -Force -Path "build/$modId.dll" -Destination $ModInstallDir
Copy-Item -Force -Path "info.json" -Destination $ModInstallDir

Write-Host "Deployed $modId (Debug) to $ModInstallDir"

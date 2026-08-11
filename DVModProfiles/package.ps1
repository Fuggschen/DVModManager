param (
	[switch]$NoArchive,
	[string]$OutputDirectory = $PSScriptRoot
)

Set-Location "$PSScriptRoot"
$FilesToInclude = "info.json","build/*","LICENSE"

$modInfo = Get-Content -Raw -Path "info.json" | ConvertFrom-Json
$modId = $modInfo.Id

$DistDir = "$OutputDirectory/dist"
if ($NoArchive) {
	$ZipWorkDir = "$OutputDirectory"
} else {
	$ZipWorkDir = "$DistDir/tmp"
}
$ZipOutDir = "$ZipWorkDir/$modId"

# Start from a clean staging dir so files removed since the last build don't linger.
if (Test-Path "$ZipOutDir") { Remove-Item -Recurse -Force "$ZipOutDir" }
New-Item "$ZipOutDir" -ItemType Directory -Force
Copy-Item -Force -Path $FilesToInclude -Destination "$ZipOutDir"

if (!$NoArchive)
{
	$FILE_NAME = "$DistDir/${modId}.zip"
	# Remove any existing archive so the zip only contains the current file set.
	if (Test-Path "$FILE_NAME") { Remove-Item -Force "$FILE_NAME" }
	Compress-Archive -CompressionLevel Fastest -Path "$ZipOutDir/*" -DestinationPath "$FILE_NAME"
}

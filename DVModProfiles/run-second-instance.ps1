# Starts a second copy of Derail Valley next to one that's already running, for joining your own
# multiplayer game on the same PC with the same Steam account.
#
# Unity Mod Manager loads mods from "Mods" next to the game, so this copy runs from a game folder of
# its own: the game's executables, its data through a junction to the real install, and a Mods
# folder of its own. That gives it its own copy of every mod's Settings.xml, and of this mod's state,
# so the host's settings land there instead of over yours. (Starting the real executable from
# another working directory isn't enough, as the game moves back to its own folder at startup.)
param(
	# The second copy's game folder, which holds its Mods folder and log
	[string]$InstanceDir = (Join-Path $env:LOCALAPPDATA "DVModProfiles\SecondInstance"),

	# Its name in multiplayer games, as both copies have the same Steam name
	[string]$Username = "Player2",

	# Copy the Mods folder over again from scratch, settings included
	[switch]$Reset,

	# Anything else to pass the game
	[string[]]$GameArgs = @()
)

$ErrorActionPreference = "Stop"
Set-Location "$PSScriptRoot"

$ReferencePath = ([xml](Get-Content -Raw -Path "Directory.Build.targets")).Project.PropertyGroup.ReferencePath.Trim()
if (!$ReferencePath) { throw "Could not read ReferencePath from Directory.Build.targets." }
$GamePath = Split-Path -Parent (Split-Path -Parent $ReferencePath)

$GameExe = Join-Path $GamePath "DerailValley.exe"
$GameMods = Join-Path $GamePath "Mods"
$InstanceExe = Join-Path $InstanceDir "DerailValley.exe"
$InstanceMods = Join-Path $InstanceDir "Mods"
$Log = Join-Path $InstanceDir "Player.log"

if (!(Test-Path $GameExe)) { throw "Derail Valley not found at $GamePath (check ReferencePath in Directory.Build.targets)." }
if (!(Test-Path (Join-Path $GameMods "Multiplayer"))) {
	throw "The Multiplayer mod isn't in $GameMods. Move it back from Mods.inactive first."
}

function Copy-Tree([string]$From, [string]$To, [string[]]$Files = @("*.*")) {
	robocopy $From $To $Files /E /NFL /NDL /NJH /NJS /NP | Out-Null
	# Robocopy's exit codes below 8 all mean it worked
	if ($LASTEXITCODE -ge 8) { throw "Copying $From to $To failed (robocopy exit code $LASTEXITCODE)." }
	$global:LASTEXITCODE = 0
}

# The game's data is too big to copy and never changes while playing, so it's shared through
# junctions. Never delete this folder recursively: that would follow them into the real install.
New-Item -ItemType Directory -Force $InstanceDir | Out-Null
foreach ($shared in @("DerailValley_Data", "MonoBleedingEdge")) {
	$link = Join-Path $InstanceDir $shared
	if (!(Test-Path $link)) {
		cmd /c mklink /J "$link" "$(Join-Path $GamePath $shared)" | Out-Null
		if ($LASTEXITCODE -ne 0) { throw "Couldn't link $link to the game's $shared folder." }
	}
}

# The executables and the mod loader's hook are small, and copied fresh each time so a game update
# reaches this copy too
Get-ChildItem -File $GamePath | Where-Object { $_.Extension -in ".exe", ".dll", ".ini" } |
	Copy-Item -Force -Destination $InstanceDir

if ($Reset -and (Test-Path $InstanceMods)) {
	Remove-Item -Recurse -Force $InstanceMods
}

$fresh = !(Test-Path $InstanceMods)
if ($fresh) {
	Write-Host "Copying $GameMods to $InstanceMods"
	Copy-Tree $GameMods $InstanceMods
}
else {
	# Keep the two copies' mods the same, as the host turns away players whose mods differ, but
	# leave this copy's settings alone: only code and manifests come across, and whole mods that
	# are new. Run with -Reset to pick up a mod's other changed files.
	$gameModNames = @(Get-ChildItem -Directory $GameMods | ForEach-Object Name)
	foreach ($name in $gameModNames) {
		$from = Join-Path $GameMods $name
		$to = Join-Path $InstanceMods $name
		if (Test-Path $to) {
			Copy-Tree $from $to @("*.dll", "info.json")
		}
		else {
			Write-Host "Copying new mod $name"
			Copy-Tree $from $to
		}
	}

	Get-ChildItem -Directory $InstanceMods | Where-Object { $gameModNames -notcontains $_.Name } | ForEach-Object {
		Write-Host "Removing mod $($_.Name), which the main copy no longer has"
		Remove-Item -Recurse -Force $_.FullName
	}
}

function Edit-Settings([string]$Mod, [scriptblock]$Change) {
	$path = Join-Path $InstanceMods "$Mod\Settings.xml"
	if (!(Test-Path $path)) { return }
	$xml = [xml](Get-Content -Raw -Path $path)
	& $Change $xml.DocumentElement
	$xml.Save($path)
}

function Set-Element($Parent, [string]$Name, [string]$Value) {
	$element = $Parent[$Name]
	if ($null -eq $element) {
		$element = $Parent.OwnerDocument.CreateElement($Name)
		[void]$Parent.AppendChild($element)
	}
	$element.InnerText = $Value
}

# Its own player to the host: the Multiplayer mod tells players apart by this GUID
$hostGuid = ([xml](Get-Content -Raw -Path (Join-Path $GameMods "Multiplayer\Settings.xml"))).Settings.Guid
Edit-Settings "Multiplayer" {
	param($settings)
	if ($fresh -or $settings.Guid -eq $hostGuid) {
		Set-Element $settings "Guid" ([guid]::NewGuid().ToString())
	}
	Set-Element $settings "UseSteamName" "false"
	Set-Element $settings "Username" $Username
}

Edit-Settings "DVModProfiles" {
	param($settings)
	# Both copies are on the same Steam account, so Steam Cloud would carry one copy's profiles and
	# settings into the other
	Set-Element $settings "useSteamCloud" "false"

	# Both copies have the same Steam ID too, so whatever the main copy chose for its own games would
	# otherwise skip the prompt here
	if ($fresh -and $null -ne $settings["hostProfilePreferences"]) {
		$settings["hostProfilePreferences"].RemoveAll()
	}
}

if ($fresh) {
	Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $InstanceMods "DVModProfiles\multiplayer-restore.json")
}

# Windowed and out of VR, to sit beside the first
$arguments = @(
	"-logFile", "`"$Log`"",
	"-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720",
	"-vrmode", "None"
) + @($GameArgs | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } })
Start-Process -FilePath $InstanceExe -WorkingDirectory $InstanceDir -ArgumentList $arguments

Write-Host "Started a second copy of Derail Valley with its mods in $InstanceMods"
Write-Host "Its log is $Log (look for 'Mods path:' to confirm which Mods folder it loaded)"

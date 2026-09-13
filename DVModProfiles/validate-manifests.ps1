<#
.SYNOPSIS
	Checks that info.json corresponds to a valid entry in repository.json.

.DESCRIPTION
	repository.json keeps an entry for every published release so that anything reading the
	manifest can still resolve an older version. This script validates that the file stays
	well formed as entries accumulate, and that the version in info.json is one of them.

	Run it with no arguments to check the working tree. Pass -Tag to additionally require that
	info.json matches the version being released.

.PARAMETER Tag
	Release tag to check info.json against. The 'dvmodprofiles-v' prefix is ignored.

.PARAMETER WarnOnly
	Report problems as warnings and exit successfully. Used for release candidate tags, whose
	versions are not expected to line up with the manifests yet.
#>
param (
	[string]$Tag,
	[switch]$WarnOnly
)

$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot"

# This mod shares a repository with the manager, so its tags carry a prefix to keep the two
# release streams apart. The version fields in the manifests do not.
$tagPrefix = 'dvmodprofiles-v'

$problems = @()

$info = Get-Content -Raw -Path "info.json" | ConvertFrom-Json
$releases = @((Get-Content -Raw -Path "repository.json" | ConvertFrom-Json).Releases)

if ($releases.Count -eq 0) {
	$problems += "repository.json has no Releases entries."
}

# The download URL is derived from the same repository info.json already points at, so a fork
# only has to change the one field.
$slug = $null
if ($info.Repository -match '^https://raw\.githubusercontent\.com/([^/]+/[^/]+)/') {
	$slug = $Matches[1]
} else {
	$problems += "info.json Repository is '$($info.Repository)', expected a https://raw.githubusercontent.com/<owner>/<repo>/... URL."
}

foreach ($release in $releases) {
	$label = if ($release.Version) { "entry '$($release.Version)'" } else { "entry with no Version" }

	if (-not $release.Id) { $problems += "repository.json $label has no Id." }
	if (-not $release.Version) { $problems += "repository.json $label has no Version." }

	if ($release.Version -and -not ($release.Version -as [version])) {
		$problems += "repository.json $label has an unparseable Version."
	}

	if ($slug -and $release.Id -and $release.Version) {
		# The download path segment is the full tag, not the bare version.
		$expectedUrl = "https://github.com/$slug/releases/download/$tagPrefix$($release.Version)/$($release.Id).zip"
		if ($release.DownloadUrl -ne $expectedUrl) {
			$problems += "repository.json $label has DownloadUrl '$($release.DownloadUrl)', expected '$expectedUrl'."
		}
	}
}

# Unity Mod Manager's standalone app collects releases into a HashSet whose equality is defined
# by Id alone, so only the first entry per Id survives and becomes the version it offers. Keeping
# newest first means that entry is the current release.
foreach ($group in ($releases | Where-Object { $_.Id -and ($_.Version -as [version]) } | Group-Object -Property Id)) {
	$versions = @($group.Group | ForEach-Object { [version]$_.Version })

	$duplicates = @($versions | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
	if ($duplicates.Count -gt 0) {
		$problems += "repository.json has duplicate entries for Id '$($group.Name)': $($duplicates -join ', ')."
	}

	for ($i = 1; $i -lt $versions.Count; $i++) {
		if ($versions[$i] -ge $versions[$i - 1]) {
			$problems += "repository.json entries for Id '$($group.Name)' must be ordered newest first, but $($versions[$i]) follows $($versions[$i - 1])."
			break
		}
	}
}

$match = $releases | Where-Object { $_.Id -eq $info.Id -and $_.Version -eq $info.Version }
if (-not $match) {
	$problems += "repository.json has no entry with Id '$($info.Id)' and Version '$($info.Version)'. Add one for this release rather than editing an existing entry."
} else {
	# Anything reading the manifest treats the newest entry as the current release, so shipping
	# an info.json that is not the newest would advertise a version we are not actually building.
	$newest = @($releases | Where-Object { $_.Id -eq $info.Id -and ($_.Version -as [version]) } | ForEach-Object { [version]$_.Version } | Sort-Object -Descending)[0]
	if ($newest -and [version]$info.Version -lt $newest) {
		$problems += "info.json Version is '$($info.Version)', but repository.json already lists a newer entry '$newest'."
	}
}

if ($Tag) {
	$expectedVersion = $Tag -replace "^$tagPrefix", ''
	if ($info.Version -ne $expectedVersion) {
		$problems += "info.json Version is '$($info.Version)', expected '$expectedVersion' to match the tag."
	}
}

if ($problems.Count -gt 0) {
	if ($WarnOnly) {
		$problems | ForEach-Object { Write-Host "::warning::$_" }
		Write-Host "::warning::Manifest problems ignored because this is a draft release."
		exit 0
	}

	$problems | ForEach-Object { Write-Host "::error::$_" }
	Write-Host "::error::info.json and repository.json are out of sync. Bump info.json and prepend a matching repository.json entry for the new version, leaving the existing entries in place."
	exit 1
}

Write-Host "info.json version $($info.Version) matches a valid repository.json entry ($($releases.Count) release(s) listed)."

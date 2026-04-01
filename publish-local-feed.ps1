param(
    [string]$PackageDir = "",
    [string]$FeedDir = "",
    [switch]$IncludeSymbols
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$resolvedPackageDir = if ([string]::IsNullOrWhiteSpace($PackageDir)) {
    Join-Path $scriptDir "artifacts\nuget"
} else {
    [System.IO.Path]::GetFullPath($PackageDir)
}

$resolvedFeedDir = if ([string]::IsNullOrWhiteSpace($FeedDir)) {
    Join-Path $scriptDir "artifacts\local-feed"
} else {
    [System.IO.Path]::GetFullPath($FeedDir)
}

if (-not (Test-Path $resolvedPackageDir)) {
    throw "Package directory not found: $resolvedPackageDir"
}

New-Item -ItemType Directory -Path $resolvedFeedDir -Force | Out-Null

$patterns = @("*.nupkg")
if ($IncludeSymbols.IsPresent) {
    $patterns += "*.snupkg"
}

$copied = @()
foreach ($pattern in $patterns) {
    Get-ChildItem -Path $resolvedPackageDir -Filter $pattern -File | ForEach-Object {
        $destination = Join-Path $resolvedFeedDir $_.Name
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        $copied += $destination
    }
}

if ($copied.Count -eq 0) {
    throw "No packages found in $resolvedPackageDir"
}

Write-Host "==> Published packages to local feed: $resolvedFeedDir"
$copied | ForEach-Object { Write-Host " - $_" }

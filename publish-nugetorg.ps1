param(
    [Parameter(Mandatory = $true)]
    [string]$ApiKey,
    [string]$PackageDir = "",
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [switch]$SkipDuplicate
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$resolvedPackageDir = if ([string]::IsNullOrWhiteSpace($PackageDir)) {
    Join-Path $scriptDir "artifacts\nuget"
} else {
    [System.IO.Path]::GetFullPath($PackageDir)
}

if (-not (Test-Path $resolvedPackageDir)) {
    throw "Package directory not found: $resolvedPackageDir"
}

$packages = Get-ChildItem -Path $resolvedPackageDir -Filter *.nupkg -File | Sort-Object Name
if ($packages.Count -eq 0) {
    throw "No .nupkg files found in $resolvedPackageDir"
}

foreach ($package in $packages) {
    $args = @(
        "nuget", "push", $package.FullName,
        "--api-key", $ApiKey,
        "--source", $Source
    )

    if ($SkipDuplicate.IsPresent) {
        $args += "--skip-duplicate"
    }

    Write-Host "==> Publishing $($package.Name)"
    dotnet @args
}

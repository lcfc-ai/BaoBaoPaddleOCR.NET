param(
    [Parameter(Mandatory = $true)]
    [string]$ApiKey,
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$OutputDir = "",
    [switch]$PackOnly,
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"

function Resolve-TargetPath {
    param(
        [string]$Value,
        [string]$Fallback
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($Value)) { $Fallback } else { $Value }
    return [System.IO.Path]::GetFullPath($candidate)
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$outDir = Resolve-TargetPath -Value $OutputDir -Fallback (Join-Path $repoRoot "artifacts\nuget")

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$packProperties = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $packProperties += "-p:BaoBaoPaddleOcrVersion=$Version"
}

if (-not $SkipPack.IsPresent) {
    Write-Host "==> Packing BaoBaoPaddleOCR.Net"
    dotnet pack (Join-Path $repoRoot "BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj") -c $Configuration -o $outDir @packProperties
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to pack BaoBaoPaddleOCR.Net"
    }

    Write-Host "==> Packing BaoBao.PaddleOCR.Models"
    dotnet pack (Join-Path $repoRoot "BaoBaoPaddleOCR.Models\BaoBaoPaddleOCR.Models.csproj") -c $Configuration -o $outDir @packProperties
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to pack BaoBao.PaddleOCR.Models"
    }
}

$resolvedVersion = $Version
if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
    [xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
    $resolvedVersion = $props.Project.PropertyGroup.BaoBaoPaddleOcrVersion
}

$mainPackage = Join-Path $outDir ("BaoBaoPaddleOCR.Net.{0}.nupkg" -f $resolvedVersion)
$modelPackage = Join-Path $outDir ("BaoBao.PaddleOCR.Models.{0}.nupkg" -f $resolvedVersion)

if ($PackOnly.IsPresent) {
    Write-Host "==> Pack completed"
    Write-Host "   $mainPackage"
    Write-Host "   $modelPackage"
    exit 0
}

foreach ($packagePath in @($mainPackage, $modelPackage)) {
    if (-not (Test-Path $packagePath)) {
        throw "Package not found: $packagePath"
    }

    Write-Host "==> Pushing $([System.IO.Path]::GetFileName($packagePath))"
    dotnet nuget push $packagePath `
        --api-key $ApiKey `
        --source $Source `
        --skip-duplicate `
        --timeout 1800

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to push package: $packagePath"
    }
}

Write-Host "==> Publish completed"

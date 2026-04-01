param(
    [string]$PaddleInferenceDir = "",
    [string]$PaddleInferenceArchive = "",
    [string]$PaddleInferenceUrl = "",
    [string]$OpenCvDir = "C:\opencv\build",
    [string]$Configuration = "Release",
    [switch]$WithModels,
    [switch]$SkipDotNetBuild
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$setupDepsArgs = @(
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $scriptDir "setup-deps.ps1"),
    "-Force"
)

if (-not [string]::IsNullOrWhiteSpace($PaddleInferenceDir)) {
    $setupDepsArgs += @("-PaddleInferenceDir", $PaddleInferenceDir)
}
elseif (-not [string]::IsNullOrWhiteSpace($PaddleInferenceArchive)) {
    $setupDepsArgs += @("-PaddleInferenceArchive", $PaddleInferenceArchive)
}
elseif (-not [string]::IsNullOrWhiteSpace($PaddleInferenceUrl)) {
    $setupDepsArgs += @("-PaddleInferenceUrl", $PaddleInferenceUrl)
}

& powershell @setupDepsArgs

if (-not $SkipDotNetBuild.IsPresent) {
    dotnet build (Join-Path $scriptDir "BaoBaoPaddleOCR.slnx") -c $Configuration
}

& powershell -ExecutionPolicy Bypass -File (Join-Path $scriptDir "build-native.ps1") `
    -Configuration $Configuration `
    -WithPaddleOcr `
    -PaddleInferenceDir (Join-Path $scriptDir "deps\paddle_inference") `
    -OpenCvDir $OpenCvDir

if ($WithModels.IsPresent) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptDir "setup-models.ps1") -Force
}

Write-Host "==> Bootstrap completed."

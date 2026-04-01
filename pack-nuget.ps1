param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [switch]$BuildNative,
    [switch]$WithPaddleOcr,
    [switch]$WithModels,
    [string]$PaddleInferenceDir = "",
    [string]$OpenCvDir = "C:\opencv\build"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$runtimeProjectPath = Join-Path $scriptDir "BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj"
$modelsProjectPath = Join-Path $scriptDir "BaoBaoPaddleOCR.Models\BaoBaoPaddleOCR.Models.csproj"
$outputPath = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $scriptDir "artifacts\nuget"
} else {
    [System.IO.Path]::GetFullPath($OutputDir)
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
Get-ChildItem -Path $outputPath -Filter *.nupkg -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $outputPath -Filter *.snupkg -File -ErrorAction SilentlyContinue | Remove-Item -Force

if ($WithModels.IsPresent) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptDir "setup-models.ps1") -Force
}

if ($BuildNative.IsPresent) {
    $nativeArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", (Join-Path $scriptDir "build-native.ps1"),
        "-Configuration", $Configuration
    )

    if ($WithPaddleOcr.IsPresent) {
        $nativeArgs += "-WithPaddleOcr"
        if (-not [string]::IsNullOrWhiteSpace($PaddleInferenceDir)) {
            $nativeArgs += @("-PaddleInferenceDir", $PaddleInferenceDir)
        }
        if (-not [string]::IsNullOrWhiteSpace($OpenCvDir)) {
            $nativeArgs += @("-OpenCvDir", $OpenCvDir)
        }
    }

    & powershell @nativeArgs
}

dotnet pack $runtimeProjectPath `
    -c $Configuration `
    -o $outputPath `
    /p:Version=$Version `
    /p:ContinuousIntegrationBuild=false

if ($WithModels.IsPresent) {
    dotnet pack $modelsProjectPath `
        -c $Configuration `
        -o $outputPath `
        /p:Version=$Version `
        /p:ContinuousIntegrationBuild=false
}

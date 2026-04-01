param(
    [string]$DestinationDir = "",
    [string]$RemoteUrl = "",
    [string]$BranchName = "main"
)

$ErrorActionPreference = "Stop"

function Resolve-GitCommand {
    $candidates = @(
        (Get-Command git -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        "C:\Program Files\Git\cmd\git.exe",
        "C:\Program Files\Git\bin\git.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "git.exe not found. Please install Git for Windows."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gitCommand = Resolve-GitCommand

if ([string]::IsNullOrWhiteSpace($DestinationDir)) {
    $DestinationDir = Join-Path (Split-Path -Parent $scriptDir) "BaoBaoPaddleOCR.NET.GitHub"
}

$destinationPath = [System.IO.Path]::GetFullPath($DestinationDir)

if (Test-Path $destinationPath) {
    Remove-Item -LiteralPath $destinationPath -Recurse -Force
}

New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null

$excludeDirs = @(
    ".git",
    ".vs",
    "deps",
    "models",
    ".model-downloads",
    "artifacts",
    "bin",
    "obj",
    "build",
    "build-default",
    "build-ninja",
    "build-visual-studio-17-2022"
)

$robocopyArgs = @(
    $scriptDir,
    $destinationPath,
    "/E",
    "/R:1",
    "/W:1",
    "/NFL",
    "/NDL",
    "/NJH",
    "/NJS",
    "/NP",
    "/XD"
) + $excludeDirs + @(
    "/XF",
    "*.nupkg",
    "*.snupkg",
    "*.log"
)

& robocopy @robocopyArgs | Out-Host
$robocopyExitCode = $LASTEXITCODE
if ($robocopyExitCode -gt 7) {
    throw "robocopy failed with exit code $robocopyExitCode"
}

& $gitCommand -C $destinationPath init -b $BranchName | Out-Host

if (-not [string]::IsNullOrWhiteSpace($RemoteUrl)) {
    & $gitCommand -C $destinationPath remote add origin $RemoteUrl | Out-Host
}

Write-Host "==> Standalone repository exported to: $destinationPath"
if (-not [string]::IsNullOrWhiteSpace($RemoteUrl)) {
    Write-Host "==> Remote origin configured: $RemoteUrl"
}

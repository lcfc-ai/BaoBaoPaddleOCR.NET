param(
    [string]$DepsRoot = "",
    [string]$PaddleOcrVersion = "3.4.0",
    [string]$PaddleOcrUrl = "",
    [string]$PaddleInferenceDir = "",
    [string]$PaddleInferenceArchive = "",
    [string]$PaddleInferenceUrl = "",
    [switch]$Force
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

function Download-File {
    param(
        [string]$Url,
        [string]$Destination
    )

    if ((Test-Path $Destination) -and -not $Force.IsPresent) {
        return
    }

    Invoke-WebRequest -Uri $Url -OutFile $Destination
}

function Expand-ArchiveToDirectory {
    param(
        [string]$ArchivePath,
        [string]$Destination
    )

    if (Test-Path $Destination) {
        if (-not $Force.IsPresent) {
            return
        }

        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    $tempDir = Join-Path ([System.IO.Path]::GetDirectoryName($ArchivePath)) ([System.IO.Path]::GetFileNameWithoutExtension($ArchivePath) + ".tmp")
    if (Test-Path $tempDir) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    tar -xf $ArchivePath -C $tempDir

    $innerDir = Get-ChildItem -Path $tempDir | Select-Object -First 1
    if ($null -eq $innerDir) {
        throw "Archive is empty: $ArchivePath"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -Path $innerDir.FullName -Force | ForEach-Object {
        Move-Item -LiteralPath $_.FullName -Destination $Destination -Force
    }

    Remove-Item -LiteralPath $tempDir -Recurse -Force
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$depsRootPath = Resolve-TargetPath -Value $DepsRoot -Fallback (Join-Path $scriptDir "deps")
$downloadDir = Join-Path $depsRootPath ".downloads"
$paddleOcrSourceUrl = if ([string]::IsNullOrWhiteSpace($PaddleOcrUrl)) {
    "https://github.com/PaddlePaddle/PaddleOCR/archive/refs/tags/v$PaddleOcrVersion.zip"
} else {
    $PaddleOcrUrl
}

New-Item -ItemType Directory -Path $depsRootPath -Force | Out-Null
New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null

$paddleOcrArchive = Join-Path $downloadDir "PaddleOCR-$PaddleOcrVersion.zip"
$paddleOcrTarget = Join-Path $depsRootPath "PaddleOCR-$PaddleOcrVersion"

Write-Host "==> Downloading PaddleOCR source"
Download-File -Url $paddleOcrSourceUrl -Destination $paddleOcrArchive

Write-Host "==> Extracting PaddleOCR source to $paddleOcrTarget"
Expand-ArchiveToDirectory -ArchivePath $paddleOcrArchive -Destination $paddleOcrTarget

$resolvedInferenceDir = ""
if (-not [string]::IsNullOrWhiteSpace($PaddleInferenceDir)) {
    $resolvedInferenceDir = [System.IO.Path]::GetFullPath($PaddleInferenceDir)
}

$paddleInferenceTarget = Join-Path $depsRootPath "paddle_inference"

if (-not [string]::IsNullOrWhiteSpace($resolvedInferenceDir)) {
    if (-not (Test-Path $resolvedInferenceDir)) {
        throw "PaddleInferenceDir not found: $resolvedInferenceDir"
    }

    if (Test-Path $paddleInferenceTarget -and $Force.IsPresent) {
        Remove-Item -LiteralPath $paddleInferenceTarget -Recurse -Force
    }

    if (-not (Test-Path $paddleInferenceTarget)) {
        Write-Host "==> Copying paddle_inference from $resolvedInferenceDir"
        Copy-Item -LiteralPath $resolvedInferenceDir -Destination $paddleInferenceTarget -Recurse
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($PaddleInferenceArchive)) {
    $resolvedArchive = [System.IO.Path]::GetFullPath($PaddleInferenceArchive)
    if (-not (Test-Path $resolvedArchive)) {
        throw "PaddleInferenceArchive not found: $resolvedArchive"
    }

    Write-Host "==> Extracting paddle_inference from local archive"
    Expand-ArchiveToDirectory -ArchivePath $resolvedArchive -Destination $paddleInferenceTarget
}
elseif (-not [string]::IsNullOrWhiteSpace($PaddleInferenceUrl)) {
    $archiveName = Split-Path -Leaf $PaddleInferenceUrl
    $archivePath = Join-Path $downloadDir $archiveName

    Write-Host "==> Downloading paddle_inference"
    Download-File -Url $PaddleInferenceUrl -Destination $archivePath

    Write-Host "==> Extracting paddle_inference to $paddleInferenceTarget"
    Expand-ArchiveToDirectory -ArchivePath $archivePath -Destination $paddleInferenceTarget
}
else {
    Write-Warning "paddle_inference was not prepared. Pass -PaddleInferenceDir, -PaddleInferenceArchive, or -PaddleInferenceUrl when you need native PaddleOCR build."
}

Write-Host "==> Dependencies root ready at: $depsRootPath"

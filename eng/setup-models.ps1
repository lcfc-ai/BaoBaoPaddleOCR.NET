param(
    [string]$ModelRoot = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Resolve-TargetPath {
    param(
        [string]$Value,
        [string]$Fallback
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($Value)) { $Fallback } else { $Value }
    $candidate = $candidate.Trim().Trim('"')
    if ($candidate.Length -gt 3) {
        $candidate = $candidate.TrimEnd('\', '/')
    }
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

function Expand-ModelArchive {
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
$repoRoot = Split-Path -Parent $scriptDir
$modelRootPath = Resolve-TargetPath -Value $ModelRoot -Fallback (Join-Path $repoRoot "models")
$downloadDir = Join-Path $repoRoot ".model-downloads"

New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
New-Item -ItemType Directory -Path $modelRootPath -Force | Out-Null

if ($Force.IsPresent) {
    @("det", "rec", "cls") | ForEach-Object {
        $legacyDir = Join-Path $modelRootPath $_
        if (Test-Path $legacyDir) {
            Remove-Item -LiteralPath $legacyDir -Recurse -Force
        }
    }
}

$models = @(
    @{
        Name = "PP-OCRv5_mobile_det_infer"
        Url = "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_mobile_det_infer.tar"
    },
    @{
        Name = "PP-OCRv5_mobile_rec_infer"
        Url = "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_mobile_rec_infer.tar"
    },
    @{
        Name = "PP-LCNet_x1_0_textline_ori_infer"
        Url = "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-LCNet_x1_0_textline_ori_infer.tar"
    }
)

foreach ($model in $models) {
    $archiveName = Split-Path -Leaf $model.Url
    $archivePath = Join-Path $downloadDir $archiveName
    $targetDir = Join-Path $modelRootPath $model.Name

    Write-Host "==> Downloading $($model.Name) model"
    Download-File -Url $model.Url -Destination $archivePath

    Write-Host "==> Extracting $($model.Name) model to $targetDir"
    Expand-ModelArchive -ArchivePath $archivePath -Destination $targetDir
}

Write-Host "==> Models are ready at: $modelRootPath"

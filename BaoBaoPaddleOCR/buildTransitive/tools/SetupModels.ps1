param(
    [Parameter(Mandatory = $true)]
    [string]$ModelRoot
)

$ErrorActionPreference = "Stop"

function Download-File {
    param(
        [string]$Url,
        [string]$Destination
    )

    if (Test-Path $Destination) {
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
        return
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

$resolvedModelRoot = [System.IO.Path]::GetFullPath($ModelRoot)
$downloadDir = Join-Path $resolvedModelRoot ".downloads"

New-Item -ItemType Directory -Path $resolvedModelRoot -Force | Out-Null
New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null

$models = @(
    @{
        Name = "PP-OCRv5_server_det_infer"
        Url = "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_server_det_infer.tar"
    },
    @{
        Name = "PP-OCRv5_server_rec_infer"
        Url = "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-OCRv5_server_rec_infer.tar"
    },
    @{
        Name = "PP-LCNet_x1_0_textline_ori_infer"
        Url = "https://paddle-model-ecology.bj.bcebos.com/paddlex/official_inference_model/paddle3.0.0/PP-LCNet_x1_0_textline_ori_infer.tar"
    }
)

foreach ($model in $models) {
    $targetDir = Join-Path $resolvedModelRoot $model.Name
    if (Test-Path $targetDir) {
        continue
    }

    $archiveName = Split-Path -Leaf $model.Url
    $archivePath = Join-Path $downloadDir $archiveName

    Write-Host "==> Downloading model: $($model.Name)"
    Download-File -Url $model.Url -Destination $archivePath

    Write-Host "==> Extracting model: $($model.Name)"
    Expand-ModelArchive -ArchivePath $archivePath -Destination $targetDir
}

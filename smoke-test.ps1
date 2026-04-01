param(
    [string]$ImagePath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ImagePath)) {
    throw "Please pass -ImagePath."
}

$slnPath = Join-Path $PSScriptRoot "BaoBaoPaddleOCR.slnx"
$cliProject = Join-Path $PSScriptRoot "BaoBaoPaddleOCR.Cli\BaoBaoPaddleOCR.Cli.csproj"

Write-Host "==> Building .NET solution"
dotnet build $slnPath | Out-Host

Write-Host "==> Running CLI in mock mode"
$env:BAOBAO_PADDLEOCR_MOCK_TEXT = "SmokeTestMockText"
dotnet run --project $cliProject -- $ImagePath --model-root $PSScriptRoot --full

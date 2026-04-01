param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$WithPaddleOcr,
    [string]$PaddleOcrSrcDir,
    [string]$PaddleInferenceDir,
    [string]$OpenCvDir = "C:\opencv\build",
    [string]$TargetArch = "win-x64",
    [switch]$WithGpu,
    [string]$CudaLibDir,
    [string]$CudnnLibDir,
    [string]$CMakeGenerator = ""
)

$ErrorActionPreference = "Stop"

function Find-CMakeCommand {
    $candidates = @()

    $fromPath = Get-Command cmake -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        $candidates += $fromPath.Source
    }

    $candidates += @(
        "C:\Program Files\CMake\bin\cmake.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    )

    foreach ($candidate in $candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Find-FirstExistingPath {
    param(
        [string[]]$Candidates
    )

    foreach ($candidate in $Candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Find-VsDevCmd {
    return Find-FirstExistingPath @(
        "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\Tools\VsDevCmd.bat",
        "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat",
        "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\Tools\VsDevCmd.bat",
        "C:\Program Files\Microsoft Visual Studio\17\Professional\Common7\Tools\VsDevCmd.bat",
        "C:\Program Files\Microsoft Visual Studio\17\Enterprise\Common7\Tools\VsDevCmd.bat",
        "C:\Program Files\Microsoft Visual Studio\17\Community\Common7\Tools\VsDevCmd.bat"
    )
}

function Find-NinjaCommand {
    $fromPath = Get-Command ninja -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    return Find-FirstExistingPath @(
        "C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
    )
}

function Import-VsDeveloperEnvironment {
    param(
        [string]$VsDevCmdPath,
        [string]$Arch = "x64"
    )

    if ([string]::IsNullOrWhiteSpace($VsDevCmdPath) -or -not (Test-Path $VsDevCmdPath)) {
        throw "VsDevCmd.bat not found: $VsDevCmdPath"
    }

    $cmdOutput = & cmd.exe /s /c "`"$VsDevCmdPath`" -arch=$Arch -host_arch=x64 >nul && set"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to initialize Visual Studio developer environment via $VsDevCmdPath"
    }

    foreach ($line in $cmdOutput) {
        $separatorIndex = $line.IndexOf("=")
        if ($separatorIndex -lt 1) {
            continue
        }

        $name = $line.Substring(0, $separatorIndex)
        $value = $line.Substring($separatorIndex + 1)
        [System.Environment]::SetEnvironmentVariable($name, $value)
    }
}

function Resolve-CMakeGenerator {
    param(
        [string]$RequestedGenerator,
        [string]$VsDevCmdPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedGenerator)) {
        return $RequestedGenerator
    }

    $vs2022Path = Find-FirstExistingPath @(
        "C:\Program Files\Microsoft Visual Studio\17\Professional",
        "C:\Program Files\Microsoft Visual Studio\17\Enterprise",
        "C:\Program Files\Microsoft Visual Studio\17\Community"
    )
    if ($null -ne $vs2022Path) {
        return "Visual Studio 17 2022"
    }

    if (-not [string]::IsNullOrWhiteSpace($VsDevCmdPath)) {
        return "Ninja"
    }

    return ""
}

$cmakeCommand = Find-CMakeCommand
if ($null -eq $cmakeCommand) {
    throw "Cannot find cmake.exe. Install CMake or Visual Studio CMake tools, then rerun this script."
}

$vsDevCmd = Find-VsDevCmd
$resolvedGenerator = Resolve-CMakeGenerator -RequestedGenerator $CMakeGenerator -VsDevCmdPath $vsDevCmd
if ($resolvedGenerator -in @("Ninja", "NMake Makefiles", "NMake Makefiles JOM")) {
    Import-VsDeveloperEnvironment -VsDevCmdPath $vsDevCmd -Arch "x64"
}

$buildDirSuffix = if ([string]::IsNullOrWhiteSpace($resolvedGenerator)) {
    "default"
} else {
    ($resolvedGenerator -replace "[^A-Za-z0-9]+", "-").Trim("-").ToLowerInvariant()
}

function Resolve-OptionalPath {
    param(
        [string]$Value,
        [string]$Fallback = ""
    )

    $candidate = if (-not [string]::IsNullOrWhiteSpace($Value)) { $Value } else { $Fallback }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        return ""
    }
    return [System.IO.Path]::GetFullPath($candidate)
}

function Copy-DllsFromDirectory {
    param(
        [string]$SourceDir,
        [string]$DestinationDir
    )

    if ([string]::IsNullOrWhiteSpace($SourceDir) -or -not (Test-Path $SourceDir)) {
        return
    }

    Get-ChildItem -Path $SourceDir -Filter *.dll -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $DestinationDir $_.Name) -Force
        }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$nativeSrcDir = Join-Path $repoRoot "BaoBaoPaddleOCR.Native"
$buildDir = Join-Path $nativeSrcDir "build-$buildDirSuffix"
$cliBinNativeDir = Join-Path $repoRoot "BaoBaoPaddleOCR.Cli\bin\$Configuration\net10.0\native"
$runtimeNativeDir = Join-Path $repoRoot "BaoBaoPaddleOCR\runtimes\$TargetArch\native"
$depsRootDir = Join-Path $repoRoot "deps"

$defaultPaddleOcrSrcDir = Join-Path $depsRootDir "PaddleOCR-3.4.0"
$PaddleOcrSrcDir = Resolve-OptionalPath -Value $PaddleOcrSrcDir -Fallback $defaultPaddleOcrSrcDir
$defaultPaddleInferenceDir = if (-not [string]::IsNullOrWhiteSpace($env:BAOBAO_PADDLE_INFERENCE_DIR)) {
    $env:BAOBAO_PADDLE_INFERENCE_DIR
} else {
    Join-Path $depsRootDir "paddle_inference"
}
$PaddleInferenceDir = Resolve-OptionalPath -Value $PaddleInferenceDir -Fallback $defaultPaddleInferenceDir
$OpenCvDir = Resolve-OptionalPath -Value $OpenCvDir
$CudaLibDir = Resolve-OptionalPath -Value $CudaLibDir -Fallback $env:BAOBAO_CUDA_LIB_DIR
$CudnnLibDir = Resolve-OptionalPath -Value $CudnnLibDir -Fallback $env:BAOBAO_CUDNN_LIB_DIR

$withPaddleValue = if ($WithPaddleOcr.IsPresent) { "ON" } else { "OFF" }
$withGpuValue = if ($WithGpu.IsPresent) { "ON" } else { "OFF" }

$cmakeArgs = @(
    "-S", $nativeSrcDir,
    "-B", $buildDir,
    "-DCMAKE_POLICY_VERSION_MINIMUM=3.5",
    "-DBAOBAO_WITH_PADDLEOCR=$withPaddleValue",
    "-DBAOBAO_WITH_GPU=$withGpuValue"
)

if ($env:OS -eq "Windows_NT" -and -not [string]::IsNullOrWhiteSpace($resolvedGenerator)) {
    $cmakeArgs += @("-G", $resolvedGenerator)

    if ($TargetArch -eq "win-x64" -and $resolvedGenerator -like "Visual Studio*") {
        $cmakeArgs += @("-A", "x64")
    }

    if ($resolvedGenerator -eq "Ninja") {
        $ninjaCommand = Find-NinjaCommand
        if (-not [string]::IsNullOrWhiteSpace($ninjaCommand)) {
            $cmakeArgs += @("-DCMAKE_MAKE_PROGRAM=$ninjaCommand")
        }

        $cmakeArgs += @("-DCMAKE_BUILD_TYPE=$Configuration")
    }
}

if ($WithPaddleOcr.IsPresent) {
    if (-not (Test-Path $PaddleOcrSrcDir)) {
        throw "PaddleOCR source directory not found: $PaddleOcrSrcDir"
    }
    if ([string]::IsNullOrWhiteSpace($PaddleInferenceDir) -or -not (Test-Path $PaddleInferenceDir)) {
        throw "PaddleInferenceDir is required when -WithPaddleOcr is used. You can pass -PaddleInferenceDir or set BAOBAO_PADDLE_INFERENCE_DIR."
    }
    if (-not (Test-Path $OpenCvDir)) {
        throw "OpenCvDir not found: $OpenCvDir"
    }

    $cmakeArgs += @(
        "-DBAOBAO_PADDLEOCR_SRC_DIR=$PaddleOcrSrcDir",
        "-DBAOBAO_PADDLE_INFERENCE_DIR=$PaddleInferenceDir",
        "-DBAOBAO_OPENCV_DIR=$OpenCvDir"
    )

    if ($WithGpu.IsPresent) {
        if ([string]::IsNullOrWhiteSpace($CudaLibDir) -or -not (Test-Path $CudaLibDir)) {
            throw "CudaLibDir is required when -WithGpu is used."
        }
        if ([string]::IsNullOrWhiteSpace($CudnnLibDir) -or -not (Test-Path $CudnnLibDir)) {
            throw "CudnnLibDir is required when -WithGpu is used."
        }

        $cmakeArgs += @(
            "-DBAOBAO_CUDA_LIB_DIR=$CudaLibDir",
            "-DBAOBAO_CUDNN_LIB_DIR=$CudnnLibDir"
        )
    }
}

$generatorDisplay = if ([string]::IsNullOrWhiteSpace($resolvedGenerator)) { "<default>" } else { $resolvedGenerator }
Write-Host "==> Using CMake generator: $generatorDisplay"
Write-Host "==> Configuring CMake (BAOBAO_WITH_PADDLEOCR=$withPaddleValue, BAOBAO_WITH_GPU=$withGpuValue)"
& $cmakeCommand @cmakeArgs | Out-Host

Write-Host "==> Building native library ($Configuration)"
& $cmakeCommand --build $buildDir --config $Configuration | Out-Host

$dllPath = Join-Path $buildDir "$Configuration\BaoBaoPaddleOCR.Native.dll"
if (-not (Test-Path $dllPath)) {
    $dllPath = Join-Path $buildDir "BaoBaoPaddleOCR.Native.dll"
}

if (-not (Test-Path $dllPath)) {
    throw "Cannot find BaoBaoPaddleOCR.Native.dll. Please check CMake build output."
}

New-Item -ItemType Directory -Path $cliBinNativeDir -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeNativeDir -Force | Out-Null

Copy-Item -Path $dllPath -Destination (Join-Path $cliBinNativeDir "BaoBaoPaddleOCR.Native.dll") -Force
Copy-Item -Path $dllPath -Destination (Join-Path $runtimeNativeDir "BaoBaoPaddleOCR.Native.dll") -Force

if ($WithPaddleOcr.IsPresent) {
    $dependencyDirs = @(
        $buildDir,
        (Join-Path $buildDir "bin"),
        (Join-Path $buildDir "clipper"),
        (Join-Path $PaddleInferenceDir "paddle\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\mklml\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\onednn\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\glog\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\gflags\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\protobuf\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\xxhash\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\yaml-cpp\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\onnxruntime\lib"),
        (Join-Path $PaddleInferenceDir "third_party\install\openvino\intel64"),
        (Join-Path $PaddleInferenceDir "third_party\install\tbb\lib"),
        (Join-Path $OpenCvDir "bin"),
        (Join-Path $OpenCvDir "x64\vc16\bin")
    )

    foreach ($dir in $dependencyDirs) {
        Copy-DllsFromDirectory -SourceDir $dir -DestinationDir $cliBinNativeDir
        Copy-DllsFromDirectory -SourceDir $dir -DestinationDir $runtimeNativeDir
    }
}

Write-Host "==> Native DLL copied to: $cliBinNativeDir"
Write-Host "==> Native DLL copied to: $runtimeNativeDir"

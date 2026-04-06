# BaoBaoPaddleOCR.NET

`BaoBaoPaddleOCR.NET` 是一个面向 Windows 的 PaddleOCR .NET 封装。
<img width="689" height="360" alt="test" src="https://github.com/user-attachments/assets/4bc6f3fd-8e4e-485a-ba58-fa37e21e14af" />

<img width="1275" height="413" alt="image" src="https://github.com/user-attachments/assets/2ed976e8-bf51-4cf2-bb37-123d581e6366" />


当前默认使用：

- `PP-OCRv5_mobile_det_infer`
- `PP-OCRv5_mobile_rec_infer`
- `PP-LCNet_x1_0_textline_ori_infer`

当前建议优先走 CPU 路线。GPU 接口已经预留，但不是本文档的重点。

## 1. 必要条件准备

### 1.1 环境要求

- Windows x64
- .NET SDK 10
- Visual Studio 2022
- C++ 桌面开发组件
- PowerShell
- CMake

### 1.2 第三方依赖

native 编译依赖以下内容：

- OpenCV
- `paddle_inference`
- PaddleOCR C++ 源码

当前仓库默认按下面的目录结构组织：

```text
src/BaoBaoPaddleOCR.NET
  BaoBaoPaddleOCR
  BaoBaoPaddleOCR.Native
  BaoBaoPaddleOCR.Cli
  PaddleOCR-3.4.0
  paddle_inference
  eng
  models
```

### 1.3 OpenCV 准备

当前脚本默认优先使用：

- `C:\opencv\build`

如果你的 OpenCV 不在这个目录，可以在构建 native 时通过 `-OpenCvDir` 传入。

### 1.4 模型准备

在子模块根目录执行：

```powershell
.\eng\setup-models.ps1 -Force
```

或者在主仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\BaoBaoPaddleOCR.NET\eng\setup-models.ps1 -Force
```

默认模型目录：

```text
models\PP-OCRv5_mobile_det_infer
models\PP-OCRv5_mobile_rec_infer
models\PP-LCNet_x1_0_textline_ori_infer
```

## 2. BaoBaoPaddleOCR.Native 和 BaoBaoPaddleOCR 编译方法

### 2.1 编译 BaoBaoPaddleOCR.Native

在子模块根目录执行：

```powershell
.\eng\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleOcrSrcDir .\PaddleOCR-3.4.0 `
  -PaddleInferenceDir .\paddle_inference
```

如果你是在主仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\BaoBaoPaddleOCR.NET\eng\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleOcrSrcDir .\src\BaoBaoPaddleOCR.NET\PaddleOCR-3.4.0 `
  -PaddleInferenceDir .\src\BaoBaoPaddleOCR.NET\paddle_inference
```

输出文件：

```text
BaoBaoPaddleOCR\runtimes\win-x64\native\BaoBaoPaddleOCR.Native.Cpu.dll
```

说明：

- 当前建议 native 一律用 `Release`
- 当前 `paddle_inference` 是 Release 依赖，直接编 `Debug native` 很容易出现运行库不匹配

### 2.2 编译 BaoBaoPaddleOCR

在子模块根目录执行：

```powershell
dotnet build .\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Debug
```

如果想直接编整个子模块解决方案：

```powershell
dotnet build .\BaoBaoPaddleOCR.slnx -c Release
```

输出文件：

```text
BaoBaoPaddleOCR\bin\Debug\net10.0\BaoBaoPaddleOCR.dll
```

### 2.3 推荐重建顺序

只改 C#：

```powershell
dotnet build .\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Debug
```

改了 native C++：

```powershell
.\eng\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleOcrSrcDir .\PaddleOCR-3.4.0 `
  -PaddleInferenceDir .\paddle_inference

dotnet build .\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Debug
```

## 3. 打包 NuGet 方法和脚本

当前打包项目：

- [BaoBaoPaddleOCR.csproj](/d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET/BaoBaoPaddleOCR/BaoBaoPaddleOCR.csproj)

### 3.1 打包命令

在主仓库根目录执行：

```powershell
New-Item -ItemType Directory -Path .\artifacts\nuget -Force | Out-Null
dotnet pack .\src\BaoBaoPaddleOCR.NET\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Release -o .\artifacts\nuget
```

当前示例产物：

```text
artifacts\nuget\BaoBaoPaddleOCR.Net.0.1.4.nupkg
```

### 3.2 一条命令脚本版

```powershell
$outDir = ".\artifacts\nuget"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
dotnet pack .\src\BaoBaoPaddleOCR.NET\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Release -o $outDir
```

## 4. 上传 NuGet 方法和脚本

### 4.1 推送到 nuget.org

```powershell
dotnet nuget push .\artifacts\nuget\BaoBaoPaddleOCR.Net.0.1.4.nupkg `
  --api-key <YOUR_NUGET_API_KEY> `
  --source https://api.nuget.org/v3/index.json `
  --skip-duplicate `
  --timeout 1800
```

说明：

- 包比较大，建议显式带上 `--timeout 1800`
- 如果网络慢，默认 300 秒超时很容易失败

### 4.2 推送到本地源或私有源

推到本地目录：

```powershell
New-Item -ItemType Directory -Path D:\local-nuget-feed -Force | Out-Null
Copy-Item .\artifacts\nuget\BaoBaoPaddleOCR.Net.0.1.4.nupkg D:\local-nuget-feed\
```

推到私有源：

```powershell
dotnet nuget push .\artifacts\nuget\BaoBaoPaddleOCR.Net.0.1.4.nupkg `
  --api-key <YOUR_API_KEY> `
  --source <YOUR_NUGET_SOURCE_URL> `
  --skip-duplicate `
  --timeout 1800
```

## 5. 第三方集成 NuGet 方法

### 5.1 安装包

```powershell
dotnet add package BaoBaoPaddleOCR.Net --version 0.1.4
```

或在项目文件中引用：

```xml
<ItemGroup>
  <PackageReference Include="BaoBaoPaddleOCR.Net" Version="0.1.4" />
</ItemGroup>
```

### 5.2 使用本地源

如果你是先本地验证 `.nupkg`，可以先添加本地源：

```powershell
dotnet nuget add source D:\local-nuget-feed --name LocalBaoBaoFeed
dotnet add package BaoBaoPaddleOCR.Net --version 0.1.4 --source D:\local-nuget-feed
```

### 5.3 第三方项目构建注意事项

- 运行环境必须是 Windows x64
- 第一次构建或第一次运行前，要确保模型目录已经准备好
- 如果你关闭了自动模型准备逻辑，需要自己提供 `models` 目录
- 建议优先用 `x64` 运行，而不是 `Any CPU`

## 6. 代码集成 Demo

### 6.1 最简单的调用

```csharp
using BaoBaoPaddleOCR;

using var client = new BaoBaoPaddleOcrClient(enableGpu: false);
var result = client.Detect("demo.png");

Console.WriteLine(result.Text);
```

### 6.2 显式传入 options

```csharp
using BaoBaoPaddleOCR;

using var client = new BaoBaoPaddleOcrClient(new BaoBaoPaddleOcrClientOptions
{
    ModelRoot = @"D:\runtime\models",
    NativeDir = @"D:\runtime\native",
    EnableGpu = false,
    EnableMkldnn = true,
    CpuThreads = 8
});

var result = client.Detect("demo.png");
Console.WriteLine(result.Text);
```

### 6.3 验证“一次加载，多次复用”

```csharp
using BaoBaoPaddleOCR;

using var client = new BaoBaoPaddleOcrClient(enableGpu: false);

for (var i = 0; i < 3; i++)
{
    var result = client.Detect("demo.png");
    Console.WriteLine($"Run {i + 1}: {result.Text}");
}
```

## 7. 其他必要说明

### 7.1 默认模型是 mobile

当前默认走 `mobile` 模型。如果你想切回 `server`：

```powershell
$env:BAOBAO_PADDLEOCR_DET_DIRNAME = "PP-OCRv5_server_det_infer"
$env:BAOBAO_PADDLEOCR_REC_DIRNAME = "PP-OCRv5_server_rec_infer"
```

### 7.2 CPU / GPU 接口说明

当前接口已经支持：

```csharp
using var cpuClient = new BaoBaoPaddleOcrClient(enableGpu: false);
using var gpuClient = new BaoBaoPaddleOcrClient(enableGpu: true, gpuDeviceId: 0);
```

当前 native 命名：

- CPU: `BaoBaoPaddleOCR.Native.Cpu.dll`
- GPU: `BaoBaoPaddleOCR.Native.Gpu.dll`

当前如果你只是先跑通项目，可以先忽略 GPU。

### 7.3 Visual Studio 常见问题

如果 VS 里提示找不到 `BaoBaoPaddleOCR.dll` 的 `ref` 文件，先执行：

```powershell
dotnet build .\src\BaoBaoPaddleOCR.NET\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Debug
dotnet build .\src\BaoBao\BaoBao.OCR\BaoBao.OCR.csproj -c Debug -p:Platform=x64
```

### 7.4 启动崩溃排查

优先检查：

- native DLL 是否已经构建
- 模型是否已经下载
- 运行目录里是否还有旧的 `BaoBaoPaddleOCR.Native.dll`

### 7.5 第一次慢、后续快是正常现象

首次调用通常会包含：

- 模型加载
- 引擎初始化
- 首次推理 warm-up

同一进程内复用同一个 `BaoBaoPaddleOcrClient` 后，后续调用通常会明显更快。

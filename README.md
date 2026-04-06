# BaoBaoPaddleOCR.NET

`BaoBaoPaddleOCR.NET` 是一个面向 Windows x64 的 PaddleOCR .NET 封装，提供：

- `BaoBaoPaddleOCR.Net`：主库，包含 .NET API 和 native 运行时资源
- `BaoBao.PaddleOCR.Models`：可选模型包，用于把默认模型随项目一起复制到输出目录

当前默认模型：

- `PP-OCRv5_mobile_det_infer`
- `PP-OCRv5_mobile_rec_infer`
- `PP-LCNet_x1_0_textline_ori_infer`

当前库的目标框架：

- `.NET Framework 4.5+`
- `.NET 8+`

注意事项：

- 当前仅支持 `Windows x64`
- 当前 native 资产按 `win-x64` 发布
- 推荐使用 `x64` 运行，不建议 `Any CPU`

## 1. 快速开始

### 1.1 安装主包

```powershell
dotnet add package BaoBaoPaddleOCR.Net --version x.y.z
```

或在项目文件中引用：

```xml
<ItemGroup>
  <PackageReference Include="BaoBaoPaddleOCR.Net" Version="x.y.z" />
</ItemGroup>
```

### 1.2 安装模型包

如果你希望模型随项目一起复制到输出目录，额外安装：

```powershell
dotnet add package BaoBao.PaddleOCR.Models --version x.y.z
```

或在项目文件中引用：

```xml
<ItemGroup>
  <PackageReference Include="BaoBao.PaddleOCR.Models" Version="x.y.z" />
</ItemGroup>
```

### 1.3 最简单的调用方式

```csharp
using BaoBaoPaddleOCR;

using (var client = new BaoBaoPaddleOcrClient(enableGpu: false))
{
    var result = client.Detect("demo.png");
    Console.WriteLine(result.Text);
}
```

### 1.4 指定模型目录和 native 目录

```csharp
using BaoBaoPaddleOCR;

var options = new BaoBaoPaddleOcrClientOptions
{
    ModelRoot = @"D:\runtime\models",
    NativeDir = @"D:\runtime\native",
    EnableGpu = false,
    EnableMkldnn = true,
    CpuThreads = 8
};

using (var client = new BaoBaoPaddleOcrClient(options))
{
    var result = client.Detect("demo.png", includeColor: true);
    Console.WriteLine(result.Text);
}
```

## 2. 模型准备方式

你可以任选下面 3 种方式。

### 2.1 方式一：安装 `BaoBao.PaddleOCR.Models`

这是最适合 NuGet 使用者的方式。安装后，模型会在构建后自动复制到输出目录下的 `models` 文件夹。

```powershell
dotnet add package BaoBao.PaddleOCR.Models --version x.y.z
```

### 2.2 方式二：使用脚本自动下载模型

在 `src/BaoBaoPaddleOCR.NET` 目录下执行：

```powershell
.\eng\setup-models.ps1 -Force
```

或者在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\BaoBaoPaddleOCR.NET\eng\setup-models.ps1 -Force
```

默认会下载到：

```text
src/BaoBaoPaddleOCR.NET\models
```

### 2.3 方式三：手动准备模型

你也可以自己把模型放到某个目录，然后在代码里指定：

```csharp
var client = new BaoBaoPaddleOcrClient(
    modelRoot: @"D:\models",
    nativeDir: @"D:\native");
```

也可以通过环境变量指定：

```powershell
$env:BAOBAO_PADDLEOCR_MODEL_ROOT = "D:\models"
```

默认查找的子目录名为：

```text
PP-OCRv5_mobile_det_infer
PP-OCRv5_mobile_rec_infer
PP-LCNet_x1_0_textline_ori_infer
```

## 3. native 依赖准备

如果你只是使用 NuGet 包，通常不需要自己编译 native。

如果你要重新编译 `BaoBaoPaddleOCR.Native`，需要先准备以下依赖：

- Visual Studio 2022
- C++ 桌面开发组件
- PowerShell
- CMake
- OpenCV
- `paddle_inference`
- PaddleOCR C++ 源码

建议的目录结构：

```text
src/BaoBaoPaddleOCR.NET
  BaoBaoPaddleOCR
  BaoBaoPaddleOCR.Native
  BaoBaoPaddleOCR.Cli
  BaoBaoPaddleOCR.Models
  PaddleOCR-3.4.0
  paddle_inference
  eng
  models
```

### 3.1 下载依赖源码和推理库

可以使用：

```powershell
.\eng\setup-deps.ps1 -Force
```

如果你已经有本地 `paddle_inference`，也可以指定：

```powershell
.\eng\setup-deps.ps1 -PaddleInferenceDir D:\third-party\paddle_inference -Force
```

### 3.2 OpenCV

构建脚本默认优先使用：

```text
C:\opencv\build
```

如果你的 OpenCV 不在这个目录，构建时通过 `-OpenCvDir` 指定即可。

## 4. 编译 BaoBaoPaddleOCR.Native

在 `src/BaoBaoPaddleOCR.NET` 目录执行：

```powershell
.\eng\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleOcrSrcDir .\PaddleOCR-3.4.0 `
  -PaddleInferenceDir .\paddle_inference
```

如果在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\BaoBaoPaddleOCR.NET\eng\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleOcrSrcDir .\src\BaoBaoPaddleOCR.NET\PaddleOCR-3.4.0 `
  -PaddleInferenceDir .\src\BaoBaoPaddleOCR.NET\paddle_inference
```

输出位置：

```text
src/BaoBaoPaddleOCR.NET\BaoBaoPaddleOCR\runtimes\win-x64\native\BaoBaoPaddleOCR.Native.Cpu.dll
```

说明：

- 当前更建议 native 使用 `Release`
- `paddle_inference` 通常也是 Release 依赖，混用 Debug/Release 比较容易出现 DLL 依赖问题

## 5. 编译主库

### 5.1 只编译主库

```powershell
dotnet build .\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Release
```

### 5.2 编译整个子模块

```powershell
dotnet build .\BaoBaoPaddleOCR.slnx -c Release
```

### 5.3 推荐重建顺序

如果只改了 C#：

```powershell
dotnet build .\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Release
```

如果改了 native C++：

```powershell
.\eng\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleOcrSrcDir .\PaddleOCR-3.4.0 `
  -PaddleInferenceDir .\paddle_inference

dotnet build .\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Release
```

## 6. NuGet 用户常见用法

### 6.1 只装主包，模型自行管理

```xml
<ItemGroup>
  <PackageReference Include="BaoBaoPaddleOCR.Net" Version="x.y.z" />
</ItemGroup>
```

运行前你需要自己准备：

- `models`
- native DLL

可以通过代码参数或环境变量指定目录。

### 6.2 主包 + 模型包

```xml
<ItemGroup>
  <PackageReference Include="BaoBaoPaddleOCR.Net" Version="x.y.z" />
  <PackageReference Include="BaoBao.PaddleOCR.Models" Version="x.y.z" />
</ItemGroup>
```

这种方式更适合大多数消费者，模型会自动复制到输出目录。

### 6.3 环境变量

```powershell
$env:BAOBAO_PADDLEOCR_NATIVE_DIR = "D:\runtime\native"
$env:BAOBAO_PADDLEOCR_MODEL_ROOT = "D:\runtime\models"
```

如果要切换模型目录别名，也可以覆盖：

```powershell
$env:BAOBAO_PADDLEOCR_DET_DIRNAME = "PP-OCRv5_server_det_infer"
$env:BAOBAO_PADDLEOCR_REC_DIRNAME = "PP-OCRv5_server_rec_infer"
$env:BAOBAO_PADDLEOCR_CLS_DIRNAME = "PP-LCNet_x1_0_textline_ori_infer"
```

## 7. 打包 NuGet

### 7.1 打包主库

在仓库根目录执行：

```powershell
New-Item -ItemType Directory -Path .\artifacts\nuget -Force | Out-Null
dotnet pack .\src\BaoBaoPaddleOCR.NET\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Release -o .\artifacts\nuget
```

### 7.2 打包模型包

```powershell
New-Item -ItemType Directory -Path .\artifacts\nuget -Force | Out-Null
dotnet pack .\src\BaoBaoPaddleOCR.NET\BaoBaoPaddleOCR.Models\BaoBaoPaddleOCR.Models.csproj -c Release -o .\artifacts\nuget
```

## 8. 推送到 NuGet

也可以直接使用仓库内置脚本一次性打包并推送两个包：

```powershell
.\eng\publish-nuget.ps1 -ApiKey <YOUR_NUGET_API_KEY>
```

如果你要显式指定版本号：

```powershell
.\eng\publish-nuget.ps1 -ApiKey <YOUR_NUGET_API_KEY> -Version 0.1.4
```

如果你想先只打包、不推送：

```powershell
.\eng\publish-nuget.ps1 -ApiKey dummy -PackOnly
```

### 8.1 推送主库

```powershell
dotnet nuget push .\artifacts\nuget\BaoBaoPaddleOCR.Net.x.y.z.nupkg `
  --api-key <YOUR_NUGET_API_KEY> `
  --source https://api.nuget.org/v3/index.json `
  --skip-duplicate `
  --timeout 1800
```

### 8.2 推送模型包

```powershell
dotnet nuget push .\artifacts\nuget\BaoBao.PaddleOCR.Models.x.y.z.nupkg `
  --api-key <YOUR_NUGET_API_KEY> `
  --source https://api.nuget.org/v3/index.json `
  --skip-duplicate `
  --timeout 1800
```

说明：

- 包较大时建议显式加上 `--timeout 1800`
- 推送前建议先本地 `dotnet pack` 验证一遍内容是否完整

## 9. 本地验证 NuGet 包

```powershell
New-Item -ItemType Directory -Path .\artifacts\local-feed -Force | Out-Null
Copy-Item .\artifacts\nuget\*.nupkg .\artifacts\local-feed\ -Force
dotnet nuget add source .\artifacts\local-feed --name LocalBaoBaoFeed
```

然后在测试项目中安装：

```powershell
dotnet add package BaoBaoPaddleOCR.Net --version x.y.z --source .\artifacts\local-feed
dotnet add package BaoBao.PaddleOCR.Models --version x.y.z --source .\artifacts\local-feed
```

## 10. 常见问题

### 10.1 为什么程序启动后找不到 native DLL

优先检查：

- 是否为 `Windows x64`
- 是否已经生成 `runtimes\win-x64\native`
- 是否通过 `BAOBAO_PADDLEOCR_NATIVE_DIR` 或 `nativeDir` 指到了正确目录
- 输出目录里是否混入了旧版 `BaoBaoPaddleOCR.Native*.dll`

### 10.2 为什么找不到模型

优先检查：

- 是否安装了 `BaoBao.PaddleOCR.Models`
- 是否执行过 `setup-models.ps1`
- `BAOBAO_PADDLEOCR_MODEL_ROOT` 是否正确
- 模型目录名是否与当前默认别名一致

### 10.3 首次调用慢，后续变快是否正常

正常。首次通常包含：

- 模型加载
- 推理引擎初始化
- 首次 warm-up

建议在同一进程里复用同一个 `BaoBaoPaddleOcrClient` 实例。

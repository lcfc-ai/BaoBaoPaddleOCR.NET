# BaoBaoPaddleOCR.NET

`BaoBaoPaddleOCR.NET` 是一个面向 Windows 的 PaddleOCR .NET 封装，当前默认使用：

- `PP-OCRv5_mobile_det_infer`
- `PP-OCRv5_mobile_rec_infer`
- `PP-LCNet_x1_0_textline_ori_infer`

当前优先支持 CPU 路线。GPU 代码已经预留，但可以后续再接。

## 你最需要先知道的事

### 1. 在主仓库里开发时，优先走本地工程引用

主仓库里的 [BaoBao.OCR.csproj](/d:/1.Work/BaoBao/src/BaoBao/BaoBao.OCR/BaoBao.OCR.csproj) 现在已经改成：

```xml
<ProjectReference Include="..\..\BaoBaoPaddleOCR.NET\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj" />
```

所以：

- 你改这个子模块的 C# 代码后，主仓库可以直接吃到
- 不需要先打 NuGet 包
- 如果 IDE 还显示旧状态，通常是缓存问题，不是代码没生效

### 2. 改 C# 和改 native，重建步骤不一样

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
```

### 3. 跑示例时建议用 x64

native 依赖放在 `win-x64` 目录下，所以建议示例程序按 `x64` 运行：

```powershell
dotnet build ..\BaoBao\BaoBao.OCR\BaoBao.OCR.csproj -c Debug -p:Platform=x64
```

## 从零跑通

### 1. 下载模型

在 [src/BaoBaoPaddleOCR.NET](/d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET) 目录执行：

```powershell
.\eng\setup-models.ps1 -Force
```

如果你是在主仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\BaoBaoPaddleOCR.NET\eng\setup-models.ps1 -Force
```

### 2. 构建 CPU native

在 [src/BaoBaoPaddleOCR.NET](/d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET) 目录执行：

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

- 这里建议用 `Release` 构建 native
- 当前 `paddle_inference` 依赖是 Release 版，强行编 `Debug native` 容易出现运行库不匹配

### 3. 构建 .NET 封装层

```powershell
dotnet build .\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Debug
```

### 4. 构建并运行主仓库示例

在主仓库根目录执行：

```powershell
dotnet build .\src\BaoBao\BaoBao.OCR\BaoBao.OCR.csproj -c Debug -p:Platform=x64
dotnet run --project .\src\BaoBao\BaoBao.OCR\BaoBao.OCR.csproj -c Debug -p:Platform=x64 -- .\src\BaoBaoPaddleOCR.NET\PaddleOCR-3.4.0\benchmark\PaddleOCR_DBNet\imgs\paper\db.jpg --repeat 3 --timing
```

## 代码使用

### 最简单的调用

```csharp
using BaoBaoPaddleOCR;

using var client = new BaoBaoPaddleOcrClient(enableGpu: false);
var result = client.Detect("demo.png");

Console.WriteLine(result.Text);
```

返回类型是 [OcrResult](/d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET/BaoBaoPaddleOCR/OcrModels.cs)，主要包含：

- `Text`
- `JsonText`
- `Blocks`

### 指定模型目录和 native 目录

```csharp
using BaoBaoPaddleOCR;

using var client = new BaoBaoPaddleOcrClient(new BaoBaoPaddleOcrClientOptions
{
    ModelRoot = @"D:\runtime\models",
    NativeDir = @"D:\runtime\native",
    EnableGpu = false
});

var result = client.Detect("demo.png");
Console.WriteLine(result.Text);
```

也可以用环境变量：

```powershell
$env:BAOBAO_PADDLEOCR_MODEL_ROOT = "D:\runtime\models"
$env:BAOBAO_PADDLEOCR_NATIVE_DIR = "D:\runtime\native"
```

### 切回 server 模型

如果你想从默认 `mobile` 改回 `server`：

```powershell
$env:BAOBAO_PADDLEOCR_DET_DIRNAME = "PP-OCRv5_server_det_infer"
$env:BAOBAO_PADDLEOCR_REC_DIRNAME = "PP-OCRv5_server_rec_infer"
```

## CPU / GPU 说明

运行时接口已经支持通过参数区分 CPU / GPU：

```csharp
using var cpuClient = new BaoBaoPaddleOcrClient(enableGpu: false);
using var gpuClient = new BaoBaoPaddleOcrClient(enableGpu: true, gpuDeviceId: 0);
```

当前 native 产物命名：

- CPU: `BaoBaoPaddleOCR.Native.Cpu.dll`
- GPU: `BaoBaoPaddleOCR.Native.Gpu.dll`

当前如果你只是要跑通项目，可以先忽略 GPU。

## 常见问题

### 1. 为什么主仓库里改了子模块代码，但像没生效

先确认主仓库项目引用的是本地工程，不是旧 NuGet 包。

当前正确方式见：

- [BaoBao.OCR.csproj](/d:/1.Work/BaoBao/src/BaoBao/BaoBao.OCR/BaoBao.OCR.csproj)

如果 Visual Studio 还没刷新：

- 重载项目
- 或重启 IDE
- 或先命令行构建一次子模块工程

### 2. 为什么 VS 提示找不到 `BaoBaoPaddleOCR.dll` 的 ref 文件

先执行：

```powershell
dotnet build .\src\BaoBaoPaddleOCR.NET\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Debug
```

再执行：

```powershell
dotnet build .\src\BaoBao\BaoBao.OCR\BaoBao.OCR.csproj -c Debug -p:Platform=x64
```

### 3. 为什么启动时崩溃

优先检查三件事：

- native DLL 是否已经构建
- 模型是否已经下载
- 运行目录里是否有旧的 `BaoBaoPaddleOCR.Native.dll` 残留

### 4. 为什么构建时提示 dll 被占用

通常是 `BaoBao.OCR.exe` 还在运行。先结束进程，再重新生成。

# BaoBaoPaddleOCR.NET

`BaoBaoPaddleOCR.NET` 是一个面向 Windows 的 PaddleOCR .NET 封装。

这份说明只讲两件事：

1. 编译步骤
2. 使用步骤

## 编译步骤

### 1. 编译 `BaoBaoPaddleOCR.dll`

这是最简单的一步。

直接用 Visual Studio 2022 打开 [BaoBaoPaddleOCR.slnx](d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET/BaoBaoPaddleOCR.slnx)，然后生成即可。

也可以使用命令行：

```powershell
dotnet build .\BaoBaoPaddleOCR.slnx -c Release
```

生成结果：

```text
BaoBaoPaddleOCR\bin\Release\net10.0\BaoBaoPaddleOCR.dll
```

### 2. 编译 `BaoBaoPaddleOCR.Native.dll`

如果你要真正运行 OCR，还需要原生层 DLL。

这一步需要先准备：

- Visual Studio 2022 的 C++ 桌面开发工具
- CMake
- OpenCV
- `paddle_inference`
- PaddleOCR C++ 源码

先准备依赖：

```powershell
.\eng\setup-deps.ps1 -PaddleInferenceDir "D:\deps\paddle_inference" -Force
```

再编译原生层：

```powershell
.\eng\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleInferenceDir .\deps\paddle_inference
```

生成结果会复制到：

```text
BaoBaoPaddleOCR\runtimes\win-x64\native\BaoBaoPaddleOCR.Native.dll
```

### 3. 下载模型

模型不会自动下载。

需要你主动执行：

```powershell
.\eng\setup-models.ps1 -Force
```

默认模型目录：

```text
models\PP-OCRv5_server_det_infer
models\PP-OCRv5_server_rec_infer
models\PP-LCNet_x1_0_textline_ori_infer
```

## 使用步骤

### 1. 运行时需要的内容

如果你是通过 `NuGet` 引用 `BaoBao.PaddleOCR`：

- `runtimes\win-x64\native\` 会跟随包一起进入输出目录
- `models\` 会在消费者项目首次构建时自动下载到输出目录

如果你不是通过 `NuGet`，而是手动拷贝文件运行 OCR，那么至少需要这些内容：

- `BaoBaoPaddleOCR.dll`
- `runtimes\win-x64\native\` 目录
- `models\` 目录

### 2. 在 C# 代码中调用

```csharp
using BaoBaoPaddleOCR;

using var client = new BaoBaoPaddleOcrClient();
var result = client.Detect("demo.png");
Console.WriteLine(result.Text);
```

### 3. 指定 native 和模型目录

如果你的目录不是默认结构，可以通过环境变量指定：

```powershell
$env:BAOBAO_PADDLEOCR_NATIVE_DIR = "D:\runtime\native"
$env:BAOBAO_PADDLEOCR_MODEL_ROOT = "D:\runtime\models"
```

### 4. 使用 CLI 验证

```powershell
dotnet run --project .\BaoBaoPaddleOCR.Cli\BaoBaoPaddleOCR.Cli.csproj -- .\demo.png --full
```

如果只是验证调用链路，不跑真实 OCR，可以用 mock：

```powershell
$env:BAOBAO_PADDLEOCR_MOCK_TEXT = "mock result"
dotnet run --project .\BaoBaoPaddleOCR.Cli\BaoBaoPaddleOCR.Cli.csproj -- .\demo.png --full
```

## NuGet 行为

如果使用者引用的是 `BaoBao.PaddleOCR` 这个包：

- native runtimes 会随包自动进入输出目录
- 模型会在首次构建时自动下载

如果你不希望自动下载模型，可以在消费者项目里关闭：

```xml
<PropertyGroup>
  <BaoBaoPaddleOCRAutoDownloadModels>false</BaoBaoPaddleOCRAutoDownloadModels>
</PropertyGroup>
```

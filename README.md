# BaoBaoPaddleOCR.NET

`BaoBaoPaddleOCR.NET` 是一个面向 Windows 的 PaddleOCR .NET 封装。

这份说明重点面向使用者，主要包括：

1. 如何编译
2. 如何通过 NuGet 使用
3. 如何在代码里调用

## 编译步骤

### 1. 编译 `BaoBaoPaddleOCR.dll`

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

如果你要构建真实 OCR 原生层，需要先准备这些依赖：

- Visual Studio 2022 的 C++ 桌面开发工具
- CMake
- OpenCV
- `paddle_inference`
- PaddleOCR C++ 源码

第三方目录建议直接放在仓库根目录，也就是和这些目录同级：

```text
BaoBaoPaddleOCR
BaoBaoPaddleOCR.Cli
BaoBaoPaddleOCR.Native
eng
PaddleOCR-3.4.0
paddle_inference
```

准备好后，执行：

```powershell
.\eng\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleOcrSrcDir .\PaddleOCR-3.4.0 `
  -PaddleInferenceDir .\paddle_inference
```

生成结果会复制到：

```text
BaoBaoPaddleOCR\runtimes\win-x64\native\BaoBaoPaddleOCR.Native.dll
```

同时还会把依赖 DLL 一起复制到：

```text
BaoBaoPaddleOCR\runtimes\win-x64\native\
```

说明：

- 推荐直接编 `Release native`
- 如果你手上的 `paddle_inference` 是官方提供的 Windows 推理库，通常也是 Release 版
- 这种情况下不要强行编 `Debug native`，否则容易出现 MSVC 运行时不匹配

### 3. 手动下载模型

如果你是源码方式运行，而不是通过 NuGet 自动准备模型，可以手动执行：

```powershell
.\eng\setup-models.ps1 -Force
```

默认模型目录：

```text
models\PP-OCRv5_server_det_infer
models\PP-OCRv5_server_rec_infer
models\PP-LCNet_x1_0_textline_ori_infer
```

## NuGet 使用

### 1. 安装包

```powershell
dotnet add package BaoBaoPaddleOCR.Net
```

或者在项目文件中引用：

```xml
<ItemGroup>
  <PackageReference Include="BaoBaoPaddleOCR.Net" Version="0.1.3" />
</ItemGroup>
```

### 2. NuGet 包会自动做什么

如果你通过 `NuGet` 使用 `BaoBaoPaddleOCR.Net`：

- native runtimes 会跟随包进入输出目录
- 模型会在消费者项目首次构建时自动下载到输出目录下的 `models\`

也就是说，普通使用者通常不需要手动拷贝 `runtimes` 和 `models`。

第一次构建如果较慢，通常是在下载模型，这是正常现象。

### 3. 如果不想自动下载模型

可以在消费者项目里关闭：

```xml
<PropertyGroup>
  <BaoBaoPaddleOCRAutoDownloadModels>false</BaoBaoPaddleOCRAutoDownloadModels>
</PropertyGroup>
```

关闭后需要你自己准备模型目录。

## 代码使用方法

### 1. 最小调用示例

```csharp
using BaoBaoPaddleOCR;

using var client = new BaoBaoPaddleOcrClient();
var result = client.Detect("demo.png");

Console.WriteLine(result.Text);
```

`Detect` 返回的是 [OcrResult](d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET/BaoBaoPaddleOCR/OcrModels.cs)，主要包含：

- `Text`：合并后的识别文本
- `JsonText`：原始 JSON 结果
- `Blocks`：逐块识别结果

### 2. 指定模型目录和 native 目录

如果你的目录不是默认结构，可以在创建客户端时显式传入：

```csharp
using BaoBaoPaddleOCR;

using var client = new BaoBaoPaddleOcrClient(
    modelRoot: @"D:\runtime\models",
    nativeDir: @"D:\runtime\native");

var result = client.Detect("demo.png");
Console.WriteLine(result.Text);
```

也可以通过环境变量指定：

```powershell
$env:BAOBAO_PADDLEOCR_NATIVE_DIR = "D:\runtime\native"
$env:BAOBAO_PADDLEOCR_MODEL_ROOT = "D:\runtime\models"
```

### 3. CLI 验证

```powershell
dotnet run --project .\BaoBaoPaddleOCR.Cli\BaoBaoPaddleOCR.Cli.csproj -- .\demo.png --full
```

如果只是验证调用链路，不跑真实 OCR，可以用 mock：

```powershell
$env:BAOBAO_PADDLEOCR_MOCK_TEXT = "mock result"
dotnet run --project .\BaoBaoPaddleOCR.Cli\BaoBaoPaddleOCR.Cli.csproj -- .\demo.png --full
```

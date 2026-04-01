# BaoBaoPaddleOCR.NET

`BaoBaoPaddleOCR.NET` 是一个基于 PaddleOCR 的本地 OCR 封装，整体调用链路是：

- PaddleOCR C++ 推理
- `BaoBaoPaddleOCR.Native` 提供稳定的 C ABI
- `BaoBaoPaddleOCR` 通过 `P/Invoke` 暴露给 .NET 使用

这个仓库现在按“源码 + 脚本”的方式维护：

- 你自己的源码保留在仓库里
- 第三方依赖和模型通过脚本下载到本地
- 不把大体积二进制和模型直接提交到 Git

## 项目结构

- `BaoBaoPaddleOCR.Native`：C++ 原生封装层
- `BaoBaoPaddleOCR`：C# 包装层
- `BaoBaoPaddleOCR.Models`：模型 NuGet 打包项目
- `BaoBaoPaddleOCR.Cli`：本地调试和验证用命令行程序
- `samples/NuGetConsumer`：包消费示例

## 适合放到 GitHub 吗

适合。

当前仓库已经按可开源的方向整理过：

- 第三方依赖改成脚本下载
- 模型目录默认不入库
- 生成目录默认不入库
- 可以先跑 stub 版本验证 `C++ -> P/Invoke -> .NET`
- 需要真实识别时，再准备 `paddle_inference`、OpenCV 和模型

## 前置要求

在 Windows 上构建时，建议准备：

- Visual Studio 2022，并安装 C++ 桌面开发工具链
- .NET 10 SDK
- CMake
- OpenCV，默认路径是 `C:\opencv\build`

真实 PaddleOCR 原生构建还需要准备：

- `paddle_inference` Windows 目录或压缩包

## 给其他人最简单的使用方式

### 方式 1：先只验证 managed + stub

这条路径最容易成功，适合其他人第一次下载仓库后快速确认环境没有问题。

```powershell
dotnet build .\BaoBaoPaddleOCR.slnx -c Release
.\build-native.ps1 -Configuration Release
```

### 方式 2：完整构建真实 PaddleOCR 封装

先准备 `paddle_inference`，然后一键初始化：

```powershell
.\bootstrap.ps1 `
  -PaddleInferenceDir "D:\deps\paddle_inference" `
  -WithModels
```

这条命令会自动完成：

- 下载 PaddleOCR 源码到 `deps/`
- 准备 `deps/paddle_inference`
- 编译 `.NET`
- 编译原生封装
- 下载模型

## 第三方依赖目录约定

脚本会把依赖准备到这些本地目录：

- `deps/PaddleOCR-3.4.0`
- `deps/paddle_inference`
- `models/`

默认模型目录结构：

- `models/PP-OCRv5_server_det_infer`
- `models/PP-OCRv5_server_rec_infer`
- `models/PP-LCNet_x1_0_textline_ori_infer`

## 手动分步构建

### 1. 准备依赖

如果你本机已经有 `paddle_inference` 目录：

```powershell
.\setup-deps.ps1 `
  -PaddleInferenceDir "D:\deps\paddle_inference" `
  -Force
```

如果你手里是压缩包：

```powershell
.\setup-deps.ps1 `
  -PaddleInferenceArchive "D:\downloads\paddle_inference-win-x64.zip" `
  -Force
```

### 2. 编译 .NET

```powershell
dotnet build .\BaoBaoPaddleOCR.slnx -c Release
```

### 3. 编译原生 stub

这个模式不接入真实 PaddleOCR，只验证 native 封装链路：

```powershell
.\build-native.ps1 -Configuration Release
```

### 4. 编译真实 PaddleOCR 原生封装

```powershell
.\build-native.ps1 `
  -Configuration Release `
  -WithPaddleOcr `
  -PaddleInferenceDir .\deps\paddle_inference
```

### 5. 下载模型

```powershell
.\setup-models.ps1 -Force
```

## 运行验证

### Mock 模式

```powershell
$env:BAOBAO_PADDLEOCR_MOCK_TEXT = "mock result"
dotnet run --project .\BaoBaoPaddleOCR.Cli\BaoBaoPaddleOCR.Cli.csproj -- .\demo.png --full
```

### 真实识别模式

```powershell
dotnet run --project .\BaoBaoPaddleOCR.Cli\BaoBaoPaddleOCR.Cli.csproj -c Release -- `
  .\deps\PaddleOCR-3.4.0\test_tipc\web\test.jpg `
  --model-root .\models `
  --native-dir .\BaoBaoPaddleOCR\runtimes\win-x64\native
```

## Visual Studio 的简单用法

如果你装了 Visual Studio，推荐这样用：

1. 第一次先执行一次 `bootstrap.ps1`
2. 然后打开 `BaoBaoPaddleOCR.slnx`
3. 日常修改 `.NET` 代码时，直接在 VS 里生成
4. 只有改了 C++ 原生层，才重新跑一次 `build-native.ps1`

也就是说，命令行主要集中在“首次准备环境”和“重编原生层”这两件事上。

## 打 NuGet 包

```powershell
.\pack-nuget.ps1 `
  -Version 0.1.0 `
  -Configuration Release `
  -BuildNative `
  -WithPaddleOcr `
  -WithModels `
  -PaddleInferenceDir .\deps\paddle_inference
```

默认输出目录：

```text
artifacts/nuget/
```

会生成两个包：

- `BaoBao.PaddleOCR`
- `BaoBao.PaddleOCR.Models`

## 发布到本地 NuGet 源

```powershell
.\publish-local-feed.ps1
```

## 发布到 NuGet.org

```powershell
.\publish-nugetorg.ps1 `
  -ApiKey "<your-nuget-api-key>" `
  -SkipDuplicate
```

## 运行时环境变量

- `BAOBAO_PADDLEOCR_NATIVE_DIR`
- `BAOBAO_PADDLEOCR_MODEL_ROOT`
- `BAOBAO_PADDLEOCR_MOCK_TEXT`
- `BAOBAO_PADDLEOCR_CPU_THREADS`
- `BAOBAO_PADDLEOCR_ENABLE_MKLDNN`

## 推荐的 GitHub 开源姿势

如果你准备把它单独放到 GitHub：

1. 把 `src/BaoBaoPaddleOCR.NET` 单独作为一个新仓库根目录
2. 保留当前的 `README.md`、`.gitignore`、`LICENSE`、`.github/workflows`
3. 不提交 `deps/`、`models/`、`artifacts/`、`build-*` 这些本地产物
4. 在 Release 页面可以额外上传编译好的 NuGet 包或运行时压缩包

这样别人下载后，至少可以先完成 stub 构建；需要真实 PaddleOCR 时，再按 README 准备完整依赖。

## 从当前大仓库导出为独立 GitHub 仓库

如果这个目录当前还在一个更大的 Git 仓库里，最简单的办法不是直接在原地再初始化 Git，而是导出一个干净副本：

```powershell
.\export-standalone-repo.ps1 `
  -DestinationDir "D:\1.Work\BaoBaoPaddleOCR.NET.GitHub" `
  -RemoteUrl "https://github.com/lcfc-ai/BaoBaoPaddleOCR.NET.git"
```

这个脚本会：

- 复制当前项目源码到一个新的独立目录
- 自动排除 `deps/`、`models/`、`artifacts/`、`bin/`、`obj/`、`build-*` 这些不该入库的内容
- 在新目录里初始化独立 Git 仓库
- 自动配置 `origin`

然后你只需要在导出的目录里执行：

```powershell
git add .
git commit -m "Initial import"
git push -u origin main
```

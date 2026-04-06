# BaoBaoPaddleOCR.NET Release Checklist

## 发版前检查

- 确认 [Directory.Build.props](/d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET/Directory.Build.props) 中的 `BaoBaoPaddleOcrVersion` 已更新
- 确认 [README.md](/d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET/README.md) 内容与当前版本一致
- 确认 [RELEASE_NOTES.md](/d:/1.Work/BaoBao/src/BaoBaoPaddleOCR.NET/RELEASE_NOTES.md) 已补充本次更新说明
- 确认 native 运行时 DLL 已准备完成
- 确认默认模型目录已准备完成，或模型包内容已更新

## 本地验证

在 `src/BaoBaoPaddleOCR.NET` 目录执行：

```powershell
dotnet build .\BaoBaoPaddleOCR\BaoBaoPaddleOCR.csproj -c Release
dotnet build .\BaoBaoPaddleOCR.Cli\BaoBaoPaddleOCR.Cli.csproj -c Release
.\eng\publish-nuget.ps1 -ApiKey dummy -PackOnly
```

建议再额外验证：

- 用一张真实图片跑一次 OCR
- 用本地源安装 `BaoBaoPaddleOCR.Net` 和 `BaoBao.PaddleOCR.Models`
- 检查输出目录中是否同时存在 `models` 与 `runtimes\win-x64\native`

## Git 提交建议

建议提交内容包含：

- 多目标框架支持：`net45`、`net8.0`
- README 中文发布说明
- 统一版本号配置
- NuGet 发布脚本

推荐 commit message：

```text
feat(paddleocr): prepare nuget release with net45 and net8 support
```

## 推送代码

在仓库根目录执行：

```powershell
git add src/BaoBaoPaddleOCR.NET
git commit -m "feat(paddleocr): prepare nuget release with net45 and net8 support"
git push
```

## 推送 NuGet

在 `src/BaoBaoPaddleOCR.NET` 目录执行：

```powershell
.\eng\publish-nuget.ps1 -ApiKey <YOUR_NUGET_API_KEY>
```

## 发版后回归

- 在一个全新测试项目中从 NuGet 安装主包和模型包
- 验证 `.NET 8` 项目可运行
- 验证 `.NET Framework 4.5+` 项目可编译并完成基本调用
- 检查 README 在 NuGet 页面显示是否正常

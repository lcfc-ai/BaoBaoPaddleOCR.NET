# BaoBaoPaddleOCR.NET Release Notes

## 0.1.4

本次版本主要完成了 NuGet 发布前的整理和兼容性增强。

### 更新内容

- 主库正式支持 `.NET Framework 4.5+` 和 `.NET 8+`
- 保留 `Windows x64` native 运行时资产打包能力
- 优化 native DLL 动态加载逻辑，兼容旧版 .NET Framework
- 补充可选模型包 `BaoBao.PaddleOCR.Models`
- 完整补齐中文 README，覆盖 NuGet 引用、模型准备、native 编译、库编译、打包与推送
- 新增统一版本号配置，方便后续发版
- 新增 `eng/publish-nuget.ps1`，可用于一键打包并推送两个 NuGet 包

### 包说明

- `BaoBaoPaddleOCR.Net`
  - 主库
  - 包含 .NET API 和 `win-x64` native 运行时
- `BaoBao.PaddleOCR.Models`
  - 模型包
  - 构建时自动将默认模型复制到输出目录

### 默认模型

- `PP-OCRv5_mobile_det_infer`
- `PP-OCRv5_mobile_rec_infer`
- `PP-LCNet_x1_0_textline_ori_infer`

### 使用建议

- 推荐在 `Windows x64` 环境下使用
- 推荐项目平台目标设置为 `x64`
- 如果想减少接入成本，建议同时引用主包和模型包

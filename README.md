<div align="center">

# Lexi

### 记住每一次遇见 · Keep the words you meet

本地优先的英语学习与词汇档案工具

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-1.2.4-blue)](windows/README.md)
[![Android](https://img.shields.io/badge/Android-0.2.2--alpha-green)](android/README.md)
[![macOS](https://img.shields.io/badge/macOS-collaboration%20reserved-lightgrey)](macos/README.md)

**[Cofran-77](https://github.com/Cofran-77) · [DespairJasper](https://github.com/DespairJasper)**

</div>

Lexi 把“遇见一个词”到“真正记住它”的过程放进同一个工具：离线查词，保存释义和来源，在自己的词汇档案中整理内容，通过复习与学习计划反复遇见它。需要扩展语境时，可以主动调用自己配置的 AI 接口；日常查词与基本档案管理不依赖 AI。

这个仓库统一整理 Windows、Android 与后续 macOS 协作入口。**各端是独立实现，不是一个安装包运行所有平台；跨端导入导出也不等于自动云同步。**

## 平台与下载

| 平台 | 当前版本 | 状态 | 源码与使用说明 |
| --- | --- | --- | --- |
| Windows | **1.2.4** | Windows 10/11 x64；桌面学习与词汇管理 | [windows/](windows/README.md) |
| Android | **0.2.2-alpha** | Android 8.0+；手机/平板原生预览版 | [android/](android/README.md) |
| macOS | 预留 | 暂无本仓库源码或安装包，由合作作者后续接入 | [macos/](macos/README.md) |

**[前往 Releases 下载](https://github.com/Cofran-77/lexi/releases)**。平台标签分别采用 `windows-vX.Y.Z`、`android-vX.Y.Z`，未来 macOS 使用 `macos-vX.Y.Z`。Android 预览 APK 使用开发签名；先备份，只有相同签名的更新才能直接覆盖保留数据。Windows 的公开安装包不含第三方 IELTS 教材与录音。

## 能做什么

- **离线查词**：内置 ECDICT 常用词子集（59,026 条），本地 SQLite 查询，不需要联网或消耗 AI Token。
- **词汇档案**：保存词义、音标、来源、原句、备注、标签及已保存的 AI 内容；支持编辑、筛选和批量管理。
- **今日重逢**：先回忆、再揭晓与评级。Windows 使用 FSRS-6 自适应排期；Android 当前沿用固定日期排期，详见下面的算法差异。
- **主动 AI 扩展**：按需生成例句、近反义词与词组，支持自行配置 OpenAI 兼容接口和模型；请求由用户触发，厂商费用由用户账户承担。
- **自己的数据自己带走**：CSV 词条迁移、兼容旧 JSON、各端本地备份；Android 提供原生 A4 PDF 导出与系统打印，Windows 提供打印与导出工具。
- **桌面学习闭环**：Windows 提供每日计划、辅助拼写、全局专注、可配置快捷键、朗读、句子翻译和金句本；这些功能并不全部存在于 Android。

### 两端的重要区别

| 能力 | Windows 1.2.4 | Android 0.2.2-alpha |
| --- | --- | --- |
| 界面技术 | C# / Avalonia / .NET 8 | Kotlin / Jetpack Compose |
| 复习排期 | FSRS-6，按稳定性、难度、历史和目标保持率计算 | 第 1、2、4、7、15 天固定日期排期 |
| 学习计划、辅助拼写、金句本 | 已实现 | 当前不提供同等桌面功能集 |
| 系统查词入口 | 可配置全局快捷键与快速卡 | Android PROCESS_TEXT / 分享文字 |
| 密钥保存 | Windows 本端保护机制 | 可选 Android Keystore 加密保存 |
| 普通词条迁移 | 29 列 CSV，兼容 JSON | 29 列 CSV，兼容旧 Windows CSV / JSON |
| 云账号 / 自动同步 | 不提供跨端云同步 | 不提供跨端云同步 |

## 界面预览

Windows 的主页面、计划编辑与小浮框局部磨砂效果：

![Windows 主界面](docs/images/windows-main.png)

![Windows 计划编辑](docs/images/windows-plan-editor.png)

截图来自隔离验收数据，表示已有界面；公开包移除了第三方教材资料，教材入口的可用内容取决于合法导入的本地资源。

## 快速开始

### 使用

1. 在 [Releases](https://github.com/Cofran-77/lexi/releases) 选择对应平台的安装包；历史节点以源码存档为主，日常使用优先最新版本。
2. 第一次使用可以直接离线查词并保存到档案，不需要 API Key。
3. 如需 AI，进入设置配置自己的接口、模型与密钥，主动点击生成。不要把密钥提交到 GitHub 或导出文件。
4. 更新、切换签名或恢复词库前，先使用应用内备份或导出功能。

### 开发

```powershell
# Windows：先构建 FSRS 助手，再启动桌面项目
cargo build --manifest-path windows/native/fsrs-optimizer/Cargo.toml --release --locked
dotnet run --project windows/Lexi.csproj
```

```sh
# Android：JDK 17 + Android SDK 35
cd android
chmod +x gradlew
./gradlew assembleDebug
```

详细工具版本、Windows 打包与 Android 签名说明见 **[构建指南](docs/BUILDING.md)**。未提供 macOS 构建命令，避免假设合作作者尚未提交的技术栈。

## 三端源码组织

```text
lexi/
├── windows/            Windows 最新源码、学习算法、测试与打包脚本
├── android/            Android 最新源码、Gradle Wrapper 与测试
├── macos/              仅协作说明；源码待合作作者接入
├── docs/               构建、架构、数据、隐私、资源、版本史
├── .github/            Issue / PR 模板与编译工作流
├── AUTHORS.md          Cofran-77 与 DespairJasper 共同署名
├── CONTRIBUTING.md     贡献与协作方式
├── THIRD_PARTY_NOTICES.md
└── LICENSE             MIT，仅覆盖项目原创代码和文档
```

11 个 Windows 与 6 个 Android 源码快照已整理进 Git 提交历史，不在目录里重复堆放完整工程。精选里程碑见 **[CHANGELOG](CHANGELOG.md)**；完整来源与归档方式见 **[版本史](docs/VERSION_HISTORY.md)**。这些提交是由已有源码交付包重建的历史，不能当作原始开发时间线。

## 关于 IELTS 资料

**本仓库和公开安装包不包含原书教材数据、录音、PDF、图片及讲义。** 项目保留教材工作区、拼写和资料管理的实现代码，但第三方资料不会因本项目采用 MIT 而变成可自由商用的内容。

如需使用教材入口，请自行准备有权使用的资源，并按 **[资源接入指南](docs/RESOURCES.md)** 配置。此前本地完整安装包含有的教材资源不属于这次公开发布。商用时也必须单独处理教材、字体、商标等相关权利。

## 隐私与数据

默认档案保存在本机。离线查词不访问 AI 服务；启用 AI 后，当前词、勾选模块及相应语境会发送到用户配置的服务商。来源原句等附加信息以各端设置为准。导出不包含 API Key。

**SQLite 备份属于各端自己的数据库格式，不能互相覆盖。** CSV 用于词条迁移，不保证复制完整复习历史、Windows FSRS 状态、计划与会话。详见 [数据迁移](docs/DATA_AND_MIGRATION.md) 和 [隐私说明](docs/PRIVACY.md)。

## 贡献、反馈与后续方向

欢迎提交问题、改进界面、修复缺陷或完善文档。请先读 [CONTRIBUTING](CONTRIBUTING.md)，提交可复现步骤，避免附带真实词库、密钥或教材文件。

当前重点是：保持 Windows 学习与弹层交互稳定、完善 Android 真机适配、由合作作者接入 macOS。路线图是协作方向，不是承诺所有功能或发布日期。

## 许可与致谢

原创代码和文档使用 **[MIT License](LICENSE)**，版权署名 **Cofran-77 和 DespairJasper**。MIT 允许个人和商业使用、修改与分发，但须保留版权及许可声明；不提供质量担保，也不授予对第三方教材或商标的额外权利。许可不需要向 GitHub“注册”，仓库中的 LICENSE 是明确的授权文本。

感谢 [ECDICT](https://github.com/skywind3000/ECDICT)、[Open Spaced Repetition / FSRS](https://github.com/open-spaced-repetition)、Avalonia、.NET、AndroidX / Jetpack Compose、Kotlin 和 OkHttp。项目的 macOS 功能参考与合作来源为 [DespairJasper/lexi-macos](https://github.com/DespairJasper/lexi-macos)。第三方许可见 [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES.md) 及各端保留的许可文件。

---

**English overview:** Lexi is a local-first vocabulary archive and English learning tool by Cofran-77 and DespairJasper. This repository contains Windows and Android source code and a reserved macOS collaboration directory. Windows uses FSRS-6; the Android preview currently uses fixed review intervals. Original project code is MIT licensed. Third-party IELTS book data and media are excluded. See the platform directories and build guide for actual capabilities and limitations.

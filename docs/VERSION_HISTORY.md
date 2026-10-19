# 源码版本史

## 2026-10-19 双平台 3.3.1

本次 `main` 发布树包含：

- Windows 3.3.1 当前源码、测试、Rust 助手、打包脚本与发布说明。
- macOS 3.3.1 完整上游源码快照，来源提交 `d6cf5758928a937b79f535e6fdcd39038bc6e5f8`。
- Windows 5 张、macOS 9 张隔离数据 README 截图。
- 跨平台 Release 标签 `v3.3.1-cross-platform`。

Windows 和 macOS 安装包作为同一 Release 的资产提供，不提交进 Git。macOS 原仓库 `DespairJasper/lexi-macos` 保持只读，不被重写或推送。

## Windows 公开快照

Windows 历史由工作区交付源码重建为有序快照，不是原始内部 Git 开发记录。

| 版本 | 主要节点 |
| --- | --- |
| 1.0.0 | 早期桌面源码基线 |
| 1.1.0 | 档案与桌面体验迭代 |
| 1.1.1 | 基线维护 |
| 1.1.2 | 今日重逢、回忆卡、快捷入库 |
| 1.1.3 | 跨端 CSV 与例句语境 |
| 1.1.4 | 学习系统与记忆桥接 |
| 1.2.0 | 桌面体验重构与长期学习整合 |
| 1.2.1 | 快捷键、全局专注、计划网格 |
| 1.2.2 | 朗读、金句、文字选择 |
| 1.2.3 | 学习阅读区、设置、辅助拼写 |
| 1.2.4 | 浮层生命周期与界面修复 |
| 3.3.1 | 当前跨平台桌面版本 |

## Android 公开快照

| 版本 | 主要节点 |
| --- | --- |
| 0.1.0-alpha | 原生移动基线 |
| 0.1.1-alpha | 保存流程迭代 |
| 0.1.2-alpha | 查词、档案与移动交互 |
| 0.2.0-alpha | 手机/平板布局与原生 A4 PDF |
| 0.2.1-alpha | CSV、PDF 文件保存 |
| 0.2.2-alpha | 跨端 CSV、选词入口、例句语境 |

Android 当前仍为预览版，未迁移 FSRS-6。

## macOS 来源历史

- 2026-10-09：首次接入 macOS 3.1.3 源码。
- 2026-10-19：更新为上游 3.3.1 快照，补齐公开跟踪的 IELTS 资源与最新截图。

旧 `MACOS-SOURCE-MANIFEST.json` 记录 3.1.3 接入节点的文件哈希，作为历史清单保留。当前完整源码以 `macos/` 目录和上游提交 `d6cf5758928a937b79f535e6fdcd39038bc6e5f8` 为准。

## 归档说明

`docs/SOURCE-MANIFEST.json` 记录历史 Windows / Android 来源包哈希，不是当前 Release 资产校验码。当前 EXE、DMG 与 `.sha256` 以 GitHub Release `v3.3.1-cross-platform` 为准。

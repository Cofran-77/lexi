# 架构与平台边界

Lexi 是双桌面平台 monorepo。Windows 和 macOS 使用相近的 C#、Avalonia、SQLite 与 Rust 技术栈，但各有独立源码、测试、资源和发布门禁，不共享所有运行时代码。

## Windows

`windows/` 的公开源码包括：

- `Domain/`、`Application/`：领域模型、学习会话、计划、记忆与迁移契约。
- `Infrastructure/`、`Services/`、`Platform/`：SQLite、备份、AI、Windows 密钥和系统集成。
- `Features/`、`Controls/`、`Shell/`、`Styles/`、`UI/`：桌面界面与交互组件。
- `native/fsrs-optimizer/`：FSRS-6 参数训练 Rust 助手与固定版本依赖。
- `tests/`、`SelfTest/`：领域、集成和 UI 自检。
- `packaging/`：自包含 publish、NSIS 安装器、校验和发行清单。

Windows 公开 Git 源码按 `.gitignore` 只保留 IELTS 资源说明与示例目录；自包含安装包按现有发行边界包含配套资料。两者资源范围不同。

## macOS

`macos/` 保持上游工程结构：

- `lexi_avalonia/`：Avalonia UI、领域逻辑、服务、平台适配和测试。
- `lexi_avalonia/Assets/`：离线词典、考试词书和上游公开跟踪的 IELTS 资源。
- `native/fsrs-optimizer/`：个人 FSRS-6 训练助手、补丁、vendor 源码和许可。
- `scripts/`：源码完整性、测试、数据备份、隐私检查、签名与 DMG 打包。
- `packaging/`：版本、签名身份和 FSRS helper pin。

macOS 的 release pin 绑定了原发布身份和已审计 helper 字节。自行发布必须生成自己的签名身份并显式更新 pin，不能绕过门禁冒充原发布身份。

## Android

Android 0.2.2-alpha 继续作为独立 Kotlin / Compose 工程保留在 `android/`。其固定日期复习规则尚未迁移桌面端 FSRS-6，不与两个桌面平台宣称行为完全一致。

## 本地数据与可选服务

词库、计划、学习轨迹、金句、统计和 FSRS 状态存储在平台本地数据库或数据目录。离线功能不依赖项目账户。

「一起学习」是可选覆盖层：

- 客户端使用公开 Supabase 项目 URL 与 publishable key。
- 用户通过 GitHub OAuth 登录后才能创建好友关系。
- 云端只接收账号资料、好友关系、当日达标和用户逐项授权的摘要。
- 完整词库、逐词历史、计划内容、API Key 和个人记忆模型留在本机。

服务端管理密钥、数据库密码、OAuth secret、真实客户端配置、用户数据库、Cookie、Session 和日志不进入公开源码。

## 发布边界

- EXE、DMG、ZIP 和 APK 不作为 Git 文件提交，只进入 Release 或既有发布渠道。
- `bin/`、`obj/`、`target/`、`publish/`、SDK、缓存和构建日志被忽略。
- 所有截图使用隔离数据目录，发布前人工检查账号、好友、通知、路径和词库内容。

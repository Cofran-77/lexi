# Lexi for Windows

Windows 10 / 11 x64 桌面版，当前应用版本 3.3.1。使用 C#、Avalonia 11.3.12 与 .NET 8，作者 Cofran-77、DespairJasper。

## 功能

- 离线 ECDICT 词典、个人词汇档案、来源和标签、批量管理。
- FSRS-6 自适应复习、今日重逢、每日计划与辅助拼写。
- 全局专注学习、正文选择与复制、可配置快捷键、朗读。
- 可选 AI 内容生成、句子翻译和金句本。
- CSV / JSON 词条迁移、打印、本端备份与恢复。
- 专题词汇、听力、语法与写作工作区，安装包附带当前配套资料。
- 考试专题：IELTS、CET-4、CET-6、考研、GMAT、TOEFL、GRE，内置 20 本考试词书、66,974 条按书独立身份保存的词条。
- 学习统计：初学 / 复习 / 拼写时长、学习词数、连续天数、峰值与易忘词，预测数据与实际数据分开呈现。
- 一起学习（可选，需登录）：一对一好友、邀请码、逐好友逐方向分享授权、共同学习日与 121 点平均稳定性预测曲线。

3.3.1 为累计升级版本，一次更新包含 Glacier Blue 视觉与布局、考试专题与 20 本词书、学习统计、一起学习、数据库 v3 加法迁移与备份空间治理，以及复习卡内部 ID、评分后“下一词”/空格无响应等修复。本地学习不依赖登录：离线词典、词书、计划、复习与 FSRS 始终可用。

## 安装与数据

从 [Releases](https://github.com/Cofran-77/lexi/releases) 下载 Windows 安装程序，包含 .NET 运行环境与 FSRS 助手。安装包未签名；Windows 可能提示未知发布者。

词库默认保存在 `%LOCALAPPDATA%\Lexi`。更新前备份并从托盘完全退出。安装更新与卸载不主动删除该数据目录；备份仍应由用户保存。安装包包含离线词典及当前专题资料，查词、个人档案和专题练习可直接使用。第三方资料的来源与限制见独立声明。

## 一起学习配置

一起学习复用现有 Supabase 项目与 GitHub OAuth，不新建后端、项目或用户池。Windows 客户端默认使用本机回环 `http://127.0.0.1:<port>/auth/callback/`，避免依赖 Windows 专用回调放行；只有本机回环监听无法启动时才注册并使用 `com.lexi.windows://auth/callback`。若启用协议回退，发布方需在同一个 Supabase 项目的 Redirect URLs 中放行该 Windows 地址。GitHub OAuth 不需要另建应用。

生产客户端的 `social-public.json`（`project_url` + `sb_publishable_...`）由发布流程注入，仓库只保留 `social-public.example.json`，已被 `.gitignore` 排除。客户端只接受 `sb_publishable_` 前缀的 publishable key；`service_role`、数据库密码和 OAuth secret 不得进入客户端、构建产物或仓库。

## 学习快捷键

← 忘记、↓ 模糊、→ 认识、↑ 朗读；Space / Enter 执行主要动作，Ctrl+Z 撤销，F11 专注，Esc 关闭当前界面。评分在揭晓后生效，输入框、菜单与文本选择按上下文处理。全局查词、翻译、收藏默认 Alt+D / Alt+T / Alt+S，可在设置修改。

## 构建

```powershell
cargo build --manifest-path native/fsrs-optimizer/Cargo.toml --release --locked
dotnet run --project Lexi.csproj
```

安装包构建和定向验证见 [构建指南](../docs/BUILDING.md)。缺少第三方教材时，依赖该资料的历史 UI 检查不能作为公开包的测试结论。

## 许可与资源

当前分发采用根目录 PolyForm Noncommercial 1.0.0；此前 MIT 版本的既有授权保留，见 [许可政策](../LICENSE-POLICY.md)。第三方许可独立保留在 Notices/ 和 FSRS vendor 目录。教材接入见 [资源指南](../docs/RESOURCES.md)。

## 源码目录

- UI/MainWindow：主窗口各功能的界面交互代码。
- Features、Controls、Shell、Styles：功能组件、控件、窗口外壳与样式。
- Domain、Application、Infrastructure、Services、Platform：数据模型、业务逻辑、存储与平台服务。
- Assets、Notices：资源及第三方声明。
- tests、SelfTest：领域测试与界面自检。
- native、packaging：FSRS 助手及安装包工具。

根目录保留启动入口、App/MainWindow 界面文件和 Lexi.csproj。

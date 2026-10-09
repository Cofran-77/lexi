# Lexi Windows 1.2.4

Windows 10/11 x64，C# + Avalonia 11.3.12 + .NET 8。作者：Cofran-77、DespairJasper。项目原创代码采用根目录 MIT 许可。

## 功能

- 59,026 条 ECDICT 本地词典、词汇档案、来源与标签、可选择复制的正文。
- 今日重逢与计划学习接入 FSRS-6；Rust 助手用于个人参数训练。样本不足时使用默认参数；并非每次启动都会训练。
- 每日计划、辅助拼写、全局专注、朗读、可配置快捷键、句子翻译与金句本。
- CSV / JSON 词条导入导出、本端备份与恢复。
- 教材与资料工作区代码保留，**第三方 IELTS 数据和录音未发布**；未配置资源时教材功能不可用，普通查词、档案与个人词汇复习可使用。

## 1.2.4 修复

菜单与确认框只在小浮框内部绘制磨砂材质，主页面保持清晰。修复原来整页模糊快照遮住新界面及菜单锚点离开视觉树后未清理的问题；保留深浅主题配色、纯色回退与模态输入隔离。

## 使用

从 [Releases](https://github.com/Cofran-77/lexi/releases/tag/windows-v1.2.4) 下载 Windows 公开安装包。它不含 IELTS 教材与录音，不能与此前本地完整资源包等同。安装无需另外安装 .NET。未签名安装包可能被 Windows 提示未知发布者，不提供商用签名认证承诺。

默认词库在 `%LOCALAPPDATA%\Lexi`。安装更新与卸载不主动删除该数据目录；仍建议先备份。关闭主窗口通常驻留托盘，完全退出使用托盘菜单。

默认学习快捷键：← 忘记、↓ 模糊、→ 认识、↑ 朗读；Space / Enter 执行主要动作，Ctrl+Z 撤销，F11 专注，Esc 关闭当前界面。评分在揭晓后生效，输入框、菜单和文本选择按上下文处理。全局查词/翻译/收藏默认 Alt+D / Alt+T / Alt+S，可在设置修改。

## 构建与验证

详见 [BUILDING](../docs/BUILDING.md)。快速启动：

```powershell
cargo build --manifest-path native/fsrs-optimizer/Cargo.toml --release --locked
dotnet run --project Lexi.csproj
```

历史验收不是本次全部重新验证。本次公开整理仅检查源码、许可排除与当前编译；旧 `--glass124-test` 等包含真实 IELTS 数据依赖的测试不能在缺少资源时直接宣称通过。定向/领域测试请在隔离数据目录执行，勿使用个人词库。

第三方许可证保留在 `Notices/` 和 `native/fsrs-optimizer/vendor/fsrs/LICENSE`。

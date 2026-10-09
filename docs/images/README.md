# 界面预览来源

Windows 图片为现有隔离演示截图，保留原文件。macOS 图片于 2026-10-09 使用实际 Lexi 3.1.3 macOS arm64 应用的内置 UI 测试生成，图像直接来自 Avalonia `RenderTargetBitmap`，未经界面改绘。所有运行使用独立 `LEXI_DATA_DIR`，没有读取个人词库或密钥。`Archive fixture`、`IELTS fixture` 为自动化测试创建的演示计划。

| 文件 | 内置模式与原始截图 | 场景 |
| --- | --- | --- |
| macos-word-card.png | focus-test / focus-front-with-details-zh.png | 回忆卡正面，三按钮评分 |
| macos-word-card-answer.png | focus-test / focus-answer-with-details-zh.png | 揭晓释义、例句与词组 |
| macos-typing.png | learning-test / typing-hints-error.png | 淡写与逐字错误反馈 |
| macos-spelling.png | learning-test / typing-no-hints-error.png | 默写出错后的正确答案 |
| macos-plans.png | learning-test / plans-manager-light.png | 独立计划管理页 |
| macos-plan-editor.png | learning-test / plans-create-dialog.png | 选择词汇、每日数量与顺序 |

复现时使用包含完整合法本地资源的 3.1.3 应用，或在 `macos/` 恢复合法 IELTS 资源后构建。测试截图同时检验真实界面逻辑，应用运行结束后退出；需在已登录的 macOS 图形桌面运行：

```sh
# 从仓库 macos/ 目录执行；DATA_ROOT 为本次独立演示目录
DATA_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/lexi-preview.XXXXXX")"
mkdir -p "$DATA_ROOT/focus" "$DATA_ROOT/learning"
LEXI_DATA_DIR="$DATA_ROOT/focus" dist/Lexi.app/Contents/MacOS/Lexi --focus-test
LEXI_DATA_DIR="$DATA_ROOT/learning" dist/Lexi.app/Contents/MacOS/Lexi --learning-test
# 核对 focus-result.txt / learning-ui-result.txt 中的 PASS/FAIL 后选取 PNG
```

此次两个模式退出码均为 0，结果文件均包含 PASS 且无 FAIL。截图仅展示相应 UI 场景，不代表全部功能和设备认证。

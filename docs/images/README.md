# 界面截图索引

本目录中的截图用于 GitHub README，只展示实际应用与隔离演示数据。截图生成时不读取作者或用户的个人词库、学习记录、登录 Cookie、通知、API Key 或本机连接配置。

## Windows 3.3.1

图片位于 `docs/images/windows/`，尺寸均为 1100 × 760。

| 文件 | 场景 |
| --- | --- |
| `ielts.png` | IELTS 专题、词书、单元与学习入口 |
| `exam-topics.png` | IELTS、CET-4、CET-6、考研、GMAT、TOEFL、GRE 分组入口 |
| `settings.png` | 设置页与学习选项 |
| `focus.png` | 全局专注学习界面 |
| `quotes.png` | 金句本 |

Windows 演示运行使用独立的 `LEXI_DATA_DIR`。该变量不得指向 `%LOCALAPPDATA%\Lexi`。

## macOS 3.3.1

图片位于 `docs/images/macos/`，原始尺寸均为 2940 × 1846。

| 文件 | 场景 |
| --- | --- |
| `home.png` | 首页、离线查词与扩展入口 |
| `exams.png` | 七大考试专题与词书目录 |
| `study-plans.png` | 学习计划总览 |
| `daily-plan.png` | 今日任务与计划进度 |
| `study-answer.png` | 单词、音标、释义与回忆反馈 |
| `study-forgot.png` | 忘记后揭示释义并再次学习 |
| `statistics.png` | 学习活动、记忆曲线与空状态 |
| `study-together.png` | 一起学习入口，截图处于未登录状态 |
| `quotes.png` | 英文金句本 |

macOS 演示运行使用独立的 `LEXI_DATA_DIR`，例如：

```sh
DEMO_DATA="$(mktemp -d "${TMPDIR:-/tmp}/lexi-preview.XXXXXX")"
LEXI_DATA_DIR="$DEMO_DATA" ./Lexi.app/Contents/MacOS/Lexi
```

截图展示的是 3.3.1 发布候选界面，不构成所有设备、系统版本或第三方 AI 服务的兼容性认证。截图文件重新采集时，应先检查画面中不含账号、好友资料、通知、词库内容、路径或密钥。

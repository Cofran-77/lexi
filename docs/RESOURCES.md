# 学习资源与第三方内容

## 离线词典

Windows 与 macOS 均包含 59,026 条 ECDICT 常用词子集，采用 MIT 许可。词典文件为平台自用的 SQLite，不包含个人词库或学习记录。

来源：[skywind3000/ECDICT](https://github.com/skywind3000/ECDICT)。许可与来源说明保留在两端 `Notices/`。

## Windows 发行边界

Windows Git 源码：

- 保留 `windows/Assets/IELTS/README.md` 与 `catalog.example.json`。
- 通过 `.gitignore` 排除第三方 IELTS 书籍、讲义和音频。
- 保留考试词书目录、离线词典与项目自有代码。

Windows 自包含安装包：

- 包含离线词典、FSRS 助手和当前专题学习资料。
- 只在发布方现有分发边界内提供资料，不自动从第三方站点抓取。

开发者如需运行依赖完整资料的本地 UI 检查，必须先确认自己拥有相关使用和构建权利，再把资料放入本地 `windows/Assets/IELTS/`。该目录不会进入公开 Git。

## macOS 公开源码

macOS 上游公开 Git 仓库跟踪 `macos/lexi_avalonia/Assets/IELTS/` 下的离线资源和音频。同步到本仓库时保留相同路径、内容和公开范围，不增加来源仓库之外的私人资料。

macOS 源码还包含：

- `Assets/dictionary.sqlite3` 离线词典。
- `Assets/Exams/` 考试词书与清单。
- `Notices/` 依赖、词典、字体和第三方资料声明。
- `native/fsrs-optimizer/vendor/fsrs/` 固定版本 FSRS 源码与许可。

## IELTS 资料

主要来源：[hefengxian/my-ielts](https://github.com/hefengxian/my-ielts)，适配基线 `5cef573933663c4673c6e0093f1df04e68018b1a`。

上游声明禁止商业用途。原书、词汇编排、音频和讲义权利归各自权利人。Lexi 保留来源，不把这些内容声明为项目原创，也不由根目录许可重新授权。来源声明不等于原权利人另行授权。

## 考试词书

考试专题包含 IELTS、CET-4、CET-6、考研、GMAT、TOEFL、GRE 与 20 本词书。每本词书使用独立学习身份，同一单词跨书不共享未完成轮次。

固定版本、来源和导入清单分别保留在：

- `windows/Assets/Exams/`
- `macos/lexi_avalonia/Assets/Exams/`
- 两端 `Notices/`

## 自备资料

Windows 可按 `catalog.example.json` 格式接入自备目录。路径相对于资源目录，解析器限制越界访问。分发或公开第三方资料前，应自行确认版权、许可和商业使用条件。

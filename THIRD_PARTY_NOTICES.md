# 第三方代码与资源说明

根目录 PolyForm Noncommercial LICENSE 与 `macos/LICENSE` 只覆盖各自适用的原创内容，不重新授权下列第三方材料。

| 内容 | 来源 / 许可 | 保留位置 |
| --- | --- | --- |
| ECDICT 常用词子集 | [skywind3000/ECDICT](https://github.com/skywind3000/ECDICT)，MIT | Windows `Notices/ECDICT-LICENSE.txt`；macOS `lexi_avalonia/Notices/ECDICT-LICENSE.txt`；Android notices |
| FSRS Rust 库 | Open Spaced Repetition，BSD-3-Clause | 两端 `native/fsrs-optimizer/vendor/fsrs/LICENSE` 及许可证索引 |
| Avalonia、.NET、SkiaSharp、HarfBuzz | 各自许可和第三方通知 | 两端 `Notices/` 与 NuGet 依赖 |
| Inter 字体 | SIL Open Font License 1.1 | Windows / macOS `Notices/INTER-LICENSE.txt` |
| AndroidX、Compose、Kotlin、协程、OkHttp | 通常为 Apache-2.0，以各发布包为准 | Gradle 依赖与 Android notices |
| Gradle Wrapper | Apache-2.0 | `android/GRADLE-LICENSE.txt` |

## IELTS 资料

IELTS 教材、词汇编排和音频来自 [hefengxian/my-ielts](https://github.com/hefengxian/my-ielts)，适配基线为 `5cef573933663c4673c6e0093f1df04e68018b1a`。上游声明禁止商业用途；原书、词汇、音频与讲义权利归各自权利人。保留来源不代表提供额外的原权利人授权。

- Windows Git 源码排除这些书籍和音频，自包含安装包随发布方现有发布边界提供。
- macOS 上游公开 Git 仓库跟踪其 IELTS 资源，源码同步保留同一公开集合。

## 考试词书

新增桌面考试词书来自上游适配数据，具体固定版本、去重和来源记录保留在：

- Windows：`windows/Assets/Exams/`、`windows/Notices/`
- macOS：`macos/lexi_avalonia/Assets/Exams/`、`macos/lexi_avalonia/Notices/`

第三方词书不由项目许可重新授权。使用、再分发或商业化前应自行核对来源条款。

## macOS

macOS 3.3.1 源码同步自 [DespairJasper/lexi-macos](https://github.com/DespairJasper/lexi-macos)。依赖声明索引见 [macOS NOTICES](macos/NOTICES.md)。

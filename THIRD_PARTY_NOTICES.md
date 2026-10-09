# 第三方代码与资源说明

根目录 MIT LICENSE 只覆盖 Cofran-77 与 DespairJasper 的项目原创代码和文档，不覆盖以下第三方内容。构建后实际分发的依赖也须保留对应通知。

| 内容 | 来源 / 许可 | 仓库中的保留位置 |
| --- | --- | --- |
| ECDICT 常用词子集 | [skywind3000/ECDICT](https://github.com/skywind3000/ECDICT)，MIT | Windows `Notices/ECDICT-LICENSE.txt`；Android `app/src/main/assets/notices/` |
| FSRS Rust 库 | Open Spaced Repetition，BSD-3-Clause | `windows/native/fsrs-optimizer/vendor/fsrs/LICENSE` |
| Avalonia | MIT（部分 native 依赖有独立通知） | Windows `Notices/`；NuGet package metadata |
| .NET、SQLite 等桌面依赖 | 各自许可 | Windows `Notices/` 及项目依赖清单 |
| SkiaSharp、HarfBuzz 与 native 组件 | 各自许可 / 第三方通知 | Windows `Notices/` |
| Inter 字体 | SIL Open Font License 1.1 | `windows/Notices/INTER-LICENSE.txt`，通过 Avalonia.Fonts.Inter 依赖引入 |
| AndroidX / Compose、Kotlin、协程、OkHttp | 通常为 Apache-2.0，按各包发布内容为准 | Gradle 声明与依赖发布包 |
| Gradle Wrapper | Apache-2.0 | Android Wrapper 与 `android/GRADLE-LICENSE.txt` |

## 不随本仓库发布的内容

此前本地项目从 [hefengxian/my-ielts](https://github.com/hefengxian/my-ielts) 参考/导入过教材词汇、录音、讲义等，原来源限制商业用途，原书版权仍属于各权利人。**本次公开仓库与 Windows 公开安装包排除了这些资源，不把它们重新授权为 MIT。** 历史版本也经过同样排除。

`windows/Notices/MY-IELTS-SOURCE.md` 只保留来源说明；教材入口源码是程序实现，不是教材分发许可。使用资源必须自行取得相应授权。

macOS 参考仓库 [DespairJasper/lexi-macos](https://github.com/DespairJasper/lexi-macos) 的许可以该仓库自身文件为准；本仓库没有导入 macOS 源码。双方作者同意本仓库原创代码使用 MIT，不自动修改其他仓库或第三方许可。

# Lexi Android · 原生预览版

以 Windows 1.1.2 为功能基线，使用 Kotlin + Jetpack Compose。面向 Android 8.0 及以上手机、平板；当前首批使用设备为 Honor Magic5 / MagicOS 10 和 MagicPad3 Pro / MagicOS 11。不是网页套壳，无账号、云同步或后台常驻。

## 安装与体验

将交付的 APK 发到设备上，用系统文件管理器打开并允许该来源安装。此版本是 **0.2.1-alpha 内部预览版**，已完成 Android 15 模拟器核心流程验收；不等同于已完成荣耀实机适配。不要卸载旧版本来更新：后续使用相同签名的 APK 覆盖安装才会保留应用私有数据。卸载会删除设备内数据，先导出档案或 Android 备份。

主页面包括查词、词汇档案、今日重逢、设置。窄窗口使用底部导航，宽窗口使用侧栏；实际按窗口宽度适配，也适用于平板分屏。软键盘弹出时主要操作保持可达。深浅主题与低饱和绿色延续桌面版，材质采用轻量半透明层次，不使用昂贵的整屏实时模糊。

- 离线查词使用随包 ECDICT；查询不访问网络、不调用 AI。离线缺词时可在档案编辑中填写释义。
- AI 独立选择例句、同义词、反义词、常用词组，明确点击生成才调用用户自己的接口。支持 DeepSeek、智谱、百炼、自定义 OpenAI 兼容接口，模型名可编辑，可取消。
- 词条包含来源、原句、备注、标签、遇见次数和 AI 内容；可编辑、批量管理、导出。
- 今日重逢只先展示单词，揭晓后评级。剩余数量与实际卡片用同一队列。周期为第 1、2、4、7、15 天，日期按设备当地日历计算。
- 在支持 Android 文本处理菜单的应用选中英文，点击菜单里的 **Lexi**；或者使用系统分享，选择 Lexi。某些应用/MagicOS 文本菜单不开放此入口时，可以复制后手动粘贴。没有全局悬浮窗或键盘监听权限。
- 打印调用 Android 系统打印服务，可选择“另存为 PDF”，也可直接导出 A4 PDF，无需打印服务。在打印时才使用临时系统 WebView 渲染本地文档；主界面为原生 Compose，无远程页面、无 JavaScript。

## 收藏、导出与文件位置

收藏只保存基础词条与释义；AI 生成只是预览，点击“保存本次 AI 内容到档案”才会写入。不会清除旧版本已经保存的 AI 内容。

PDF 默认是 A4 艾宾浩斯复习表，包含单词、音标、释义和第 1、2、4、7、15 天打卡框。完整档案 PDF 是单独的次要选项，包含来源、备注与已保存 AI。

PDF 自动保存在手机文件管理器的 **下载 / Lexi词汇库**。保存后顶部显示“打开 PDF / 分享 PDF”；无 PDF 阅读器时可先分享。Android 10 及以上无需存储权限；Android 8–9 首次需允许存储权限。不同 PDF 自动使用不同文件名，不覆盖旧导出。

## CSV 迁移与备份

普通词条导入导出统一使用 CSV，在设置“数据与导出”中操作；保存 CSV 时可选择位置。UTF-8 编码带 BOM，可用 Excel/WPS 打开。最低只需“单词,释义”两列，也支持 word,translation。完整导出包含 UUID、来源、备注、标签、AI 和当前复习进度，不含 API Key。重复 UUID/单词会跳过，不覆盖现有内容；无效文件整体拒绝。

为保留旧版 Windows 迁移能力，导入仍兼容已有 Lexi 档案 JSON，界面不再把 JSON 作为普通导出项。Windows 端是否支持 CSV 取决于其现有版本，本轮未修改 Windows。

Android SQLite 备份用于本端整库恢复，包括撤销记录。恢复会替换当前档案，操作前需要确认，并保留恢复前安全副本。不能使用 Windows vocab.sqlite3 直接覆盖。词条迁移不等于同步，也不包含完整历史复习流水。

## API Key 与隐私

默认密钥只在本次运行内存中保留。明确勾选记住后由 Android Keystore AES-GCM 加密保存。没有纯文本回退，不进入 CSV/PDF/SQLite 备份。应用禁用系统自动备份。用户只有开启“附带来源原句”时，生成请求才附带该原句。AI 仍会发送当前词、勾选模块与语境偏好到用户配置的厂商；该厂商费用由用户账户承担。

## 开发与构建

Windows/macOS/Linux 均可编译 Android APK；**不需要借 Mac**。推荐 Android Studio（安装 JDK17、Android SDK35、Build Tools35），打开本目录，等依赖下载完成。

```powershell
# JAVA_HOME 指向 JDK17；ANDROID_HOME 指向 Android SDK
./gradlew.bat assembleDebug
```

```sh
chmod +x gradlew
./gradlew assembleDebug
```

APK 输出：`app/build/outputs/apk/debug/app-debug.apk`。首次依赖下载较大；工程配置了阿里 Maven 镜像及官方后备源。Gradle Wrapper8.9、AGP8.7.3、Kotlin2.0.21、Compose BOM2024.12.01。本机 SDK 路径的 `local.properties` 不随源码交付。

## 发布边界

当前交付为开发签名预览 APK，不是应用商店正式版。正式公开投放前需保管独立长期 release 签名、确定包版本策略和隐私声明，并做真机验收；Android 签名不等于购买 Windows 商业签名证书。默认 debug keystore 不放进源码/分发包。未来如果切换签名，Android 将拒绝覆盖，须先导出数据再迁移到正式版，不能以卸载操作冒充无损升级。

本轮已按用户最新要求运行 Android 15 模拟器验收，包括离线词典、数据库保存/复习/撤销/备份恢复、手机编辑与复习、平板深色双栏，以及直接 PDF 文件保存。中文音标及多页 PDF 已实际渲染查看。未测试荣耀实机、真实付费 AI、大量词条压力及全部系统版本；详见交付说明。

第三方词典许可见 `app/src/main/assets/notices`。Compose/AndroidX、Kotlin、OkHttp 等通过 Maven 引入；本包不包含 Windows 私人词库或密钥。


## 公开源码归档

本目录对应 Android 0.2.1 源码快照。项目作者：Cofran-77、DespairJasper；原创代码适用根目录 MIT 许可。第三方许可独立保留。测试声明来自当时交付记录，本次归档不代表重新执行所有测试。

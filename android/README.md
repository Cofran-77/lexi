# Lexi Android · 原生预览版

以 Windows 1.1.2 为功能基线，使用 Kotlin + Jetpack Compose。面向 Android 8.0 及以上手机、平板；当前首批使用设备为 Honor Magic5 / MagicOS 10 和 MagicPad3 Pro / MagicOS 11。不是网页套壳，无账号、云同步或后台常驻。

## 安装与体验

将交付的 APK 发到设备上，用系统文件管理器打开并允许该来源安装。此版本是 **0.2.0-alpha 内部预览版**，已完成 Android 15 模拟器核心流程验收；不等同于已完成荣耀实机适配。不要卸载旧版本来更新：后续使用相同签名的 APK 覆盖安装才会保留应用私有数据。卸载会删除设备内数据，先导出档案或 Android 备份。

主页面包括查词、词汇档案、今日重逢、设置。窄窗口使用底部导航，宽窗口使用侧栏；实际按窗口宽度适配，也适用于平板分屏。软键盘弹出时主要操作保持可达。深浅主题与低饱和绿色延续桌面版，材质采用轻量半透明层次，不使用昂贵的整屏实时模糊。

- 离线查词使用随包 ECDICT；查询不访问网络、不调用 AI。离线缺词时可在档案编辑中填写释义。
- AI 独立选择例句、同义词、反义词、常用词组，明确点击生成才调用用户自己的接口。支持 DeepSeek、智谱、百炼、自定义 OpenAI 兼容接口，模型名可编辑，可取消。
- 词条包含来源、原句、备注、标签、遇见次数和 AI 内容；可编辑、批量管理、导出。
- 今日重逢只先展示单词，揭晓后评级。剩余数量与实际卡片用同一队列。周期为第 1、2、4、7、15 天，日期按设备当地日历计算。
- 在支持 Android 文本处理菜单的应用选中英文，点击菜单里的 **Lexi**；或者使用系统分享，选择 Lexi。某些应用/MagicOS 文本菜单不开放此入口时，可以复制后手动粘贴。没有全局悬浮窗或键盘监听权限。
- 打印调用 Android 系统打印服务，可选择“另存为 PDF”，也可直接导出 A4 PDF，无需打印服务。在打印时才使用临时系统 WebView 渲染本地文档；主界面为原生 Compose，无远程页面、无 JavaScript。

## Windows 词条转入

在 Windows Lexi 设置中导出完整档案 **JSON**，传到手机，再在 Android 设置中选择导入档案。UUID、释义、来源、备注、标签、AI 内容和当前复习进度可转移；重复 UUID/单词会跳过，不静默覆盖。JSON 不含 API Key，也不包含完整历史复习流水。

Android 的 SQLite 备份用于 **Android 版整库恢复**，包含本端撤销记录，不可用 Windows 的 vocab.sqlite3 直接覆盖。Windows 1.1.2 是否提供反向 JSON 入库入口取决于该版本已有功能，本项目不自动改动 Windows。没有后台双向同步。

备份恢复会替换本设备档案，UI 会确认；恢复前保留旧库安全副本。迁移/校验失败时停止操作，不自动清空词库。应用更新只改变程序，不替换用户库。首次预览请先用导出的副本尝试。

## API Key 与隐私

默认密钥只在本次运行内存中保留。明确勾选记住后由 Android Keystore AES-GCM 加密保存。没有纯文本回退，不进入 JSON/PDF/SQLite 备份。应用禁用系统自动备份。用户只有开启“附带来源原句”时，生成请求才附带该原句。AI 仍会发送当前词、勾选模块与语境偏好到用户配置的厂商；该厂商费用由用户账户承担。

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

本目录对应 Android 0.2.0 源码快照。项目作者：Cofran-77、DespairJasper；原创代码适用根目录 MIT 许可。第三方许可独立保留。测试声明来自当时交付记录，本次归档不代表重新执行所有测试。

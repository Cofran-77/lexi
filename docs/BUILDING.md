# 构建与发布

## Windows

开发环境：Windows 10/11 x64、.NET SDK 8、Rust stable（使用 FSRS Rust 库的 edition 2024，至少 Rust 1.85；建议当前稳定版本）。打包另需 NSIS 3 Unicode。NuGet、Cargo 首次还原需要联网。

```powershell
git clone https://github.com/Cofran-77/lexi.git
cd lexi
cargo build --manifest-path windows/native/fsrs-optimizer/Cargo.toml --release --locked
dotnet build windows/Lexi.csproj -c Release
dotnet run --project windows/Lexi.csproj
```

Rust 助手输出为 `windows/native/fsrs-optimizer/target/release/fsrs-optimizer.exe`，桌面项目在文件存在时复制它；打包脚本要求助手已经构建。仓库不跟踪这个二进制文件，但公开 Windows 安装包包含该助手。

生成自包含安装包：

```powershell
pwsh -File windows/packaging/build-installer.ps1 -Version 1.2.4 -Dotnet dotnet
pwsh -File windows/packaging/package-release.ps1 -Version 1.2.4
```

默认输出到仓库根目录 `Lexi-1.2.4-Windows-x64/` 和相应源码/分发 ZIP。Git 工程保留资源格式示例；构建完整专题版本时，先安装当前 Windows 版，将安装目录中的 Assets/IELTS 内容复制到 windows/Assets/IELTS。现有构建规则会复制资源到输出；普通用户直接安装完整 Windows 安装包即可。安装程序需要 NSIS 位于默认路径或通过 `-Nsis` 指定。

### 测试

按改动选择检查，不要求每次都运行全软件测试：领域测试位于 `windows/tests/`，UI/交互自检位于 `windows/SelfTest/`。例如：

```powershell
dotnet run --project windows/tests/MemoryTests/MemoryTests.csproj -c Release
```

某些历史 UI 测试依赖未公开的 IELTS 资源。运行自检必须使用隔离 `LEXI_DATA_DIR`，不要指向个人词库；测试入口与所需资源需先读相应测试源码。本仓库工作流验证当前工程编译，不把编译成功当成全功能验收。

## Android

环境：JDK 17、Android SDK Platform 35、Build Tools 35。工程使用 Gradle Wrapper 8.9、AGP 8.7.3、Kotlin 2.0.21、Compose BOM 2024.12.01。Android Studio 打开 `android/` 即可；本机 SDK 路径放到不提交的 `local.properties`。

```powershell
cd android
./gradlew.bat assembleDebug
```

```sh
cd android
chmod +x gradlew
./gradlew assembleDebug
```

输出：`app/build/outputs/apk/debug/app-debug.apk`。依赖仓库保留工程已有阿里镜像与官方源配置；不需要 macOS 编译环境。

模拟器/真机仪器测试在 `app/src/androidTest`，可按改动执行 `./gradlew connectedDebugAndroidTest`。需要连接设备，不代表 CI 已认证全部 Android / MagicOS 版本。

### 签名与升级

当前 Release 提供 Android 源码与已有开发签名 APK。开发私钥不公开。本地生成的 debug APK 可能使用不同签名，不能保证覆盖已安装的旧 APK。正式分发需要维护自己的长期 release 签名；切换签名前先导出数据，卸载前应导出数据。

## macOS

当前接入 3.1.3，运行面向 Apple Silicon / macOS 12+。开发需要 macOS 与 .NET SDK 8；个人 FSRS 助手另需 Rust 工具链。全部入口从 `macos/` 执行：

```sh
cd macos
dotnet build lexi_avalonia/Lexi.csproj -c Release
bash 启动Lexi源码.command
python3 scripts/verify_source_integrity.py
# 提供合法本地 IELTS 资源后，运行完整领域与 UI 回归
bash scripts/run_tests.sh
# 构建助手：使用自己已安装的 Rust 工具链
bash scripts/build_fsrs_optimizer.sh
```

本机 SDK 可放在 `macos/.tools/dotnet/`，或使用 `DOTNET_ROOT` / PATH；不提交 SDK。Git 中的 IELTS 目录只保留结构示例，不含教材或音频；完整专题测试依赖原资源，见 [资源说明](RESOURCES.md)。构建应用本身不要求助手二进制存在；完整打包要求先构建并验证助手。

签名与发布脚本在 `macos/scripts/`，配置在 `macos/packaging/`。现有 release pin 和 helper SHA256 绑定原发布身份与已审计助手；自己的发布身份须显式更新 pin 并完成审计，不能直接复用作者的签名证明。完整签名、DMG、数据保留和校验步骤见 [macOS README](../macos/README.md)。

本仓库 CI 目前仍仅编译 Windows / Android。macOS 的本地验证单独记录于 [验证记录](VERIFICATION.md)，不宣称 CI 已覆盖 macOS。

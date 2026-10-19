# 构建与发布

## Windows

### 环境

- Windows 10 / 11 x64。
- .NET SDK 8。
- Rust 1.85+，FSRS 依赖使用 Rust 2024 edition。
- NSIS 3 Unicode，用于安装包。
- NuGet 与 Cargo 首次还原需要联网。

### 编译

```powershell
git clone https://github.com/Cofran-77/lexi.git
cd lexi
cargo build --manifest-path windows/native/fsrs-optimizer/Cargo.toml --release --locked
dotnet build windows/Lexi.csproj -c Release
dotnet run --project windows/Lexi.csproj
```

Rust 助手输出为 `windows/native/fsrs-optimizer/target/release/fsrs-optimizer.exe`。它不进入 Git，但正式安装包必须包含。

### 安装包

```powershell
pwsh -File windows/packaging/build-installer.ps1 `
  -Version 3.3.1 `
  -Dotnet <dotnet-path> `
  -Nsis <makensis-path>
```

如需把合法的公开客户端配置注入安装包，可增加：

```powershell
-SocialConfigPath <social-public.json>
```

该 JSON 只应包含公开项目 URL 和 `sb_publishable_` 客户端 key。真实配置文件不提交，示例为 `windows/social-public.example.json`。`service_role`、数据库密码和 OAuth secret 不得放进客户端。

默认输出为仓库根目录的 `Lexi-3.3.1-Windows-x64/Lexi-3.3.1-x64-setup.exe`。发布资产还应生成：

```text
Lexi-3.3.1-x64-setup.exe.sha256
```

### 测试

```powershell
dotnet run --project windows/tests/MemoryTests/MemoryTests.csproj -c Release
dotnet run --project windows/tests/LearningTests/LearningTests.csproj -c Release
```

UI / 自检程序必须使用隔离 `LEXI_DATA_DIR`：

```powershell
$env:LEXI_DATA_DIR = Join-Path $env:TEMP ('lexi-test-' + [guid]::NewGuid())
dotnet run --project windows/Lexi.csproj -c Release -- --visual123-test
Remove-Item Env:LEXI_DATA_DIR
```

测试入口和所需资源以源码为准。依赖未公开 IELTS 资料的历史 UI 测试不能当作公开源码树的默认验收。当前 Windows Release 已知有一个 1100 宽度的 exam topics grid placement 断言失败，不能宣称全量 UI 测试通过。

## macOS

### 环境

- Apple Silicon Mac，macOS 12+。
- .NET SDK 8。
- Python 3.10+。
- 完整打包需要 Rust、Cargo 缓存和 macOS DMG 工具。

### 编译与完整性

```sh
cd macos
python3 scripts/verify_source_integrity.py
dotnet build lexi_avalonia/Lexi.csproj -c Release
bash scripts/run_tests.sh
```

源码入口：

```sh
cd macos
bash 启动Lexi源码.command
```

### 发布

```sh
cd macos
bash scripts/release.sh
bash scripts/package_dmg.sh --no-build
```

`release.sh` 会执行回归、签名、隔离数据验收、许可证/隐私检查和发布证明。现有 pin 绑定原发布身份与 helper，自行签名发布时必须先：

1. 重建并审计 `fsrs-optimizer`。
2. 更新 `packaging/fsrs-helper-pin.json`。
3. 生成自己的签名身份。
4. 更新 `packaging/release-pin.json` 的 designated requirement。

详细步骤与数据保留说明见 [macOS README](../macos/README.md)。

## Android

环境为 JDK 17、Android SDK Platform 35、Build Tools 35、Gradle Wrapper 8.9。

```powershell
cd android
.\gradlew.bat assembleDebug
```

```sh
cd android
chmod +x gradlew
./gradlew assembleDebug
```

输出为 `android/app/build/outputs/apk/debug/app-debug.apk`。Android 仍是 0.2.2-alpha 预览，不等同桌面端 FSRS-6 功能集。

## CI

`.github/workflows/build.yml` 分别编译 Windows、macOS 和 Android。CI 只验证源码可编译，不替代安装包签名、真实 OAuth、好友同步、AI 服务或真机验收。

## 发布检查

1. 运行平台构建和定向测试。
2. 使用隔离数据生成截图。
3. 生成 EXE / DMG 与 SHA256。
4. 扫描 Git 树，排除数据库、Cookie、Session、Token、日志和签名材料。
5. 检查 README 图片与本地链接。
6. 推送源码和标签。
7. 创建 Release 并上传二进制，再回下载核对大小、哈希和页面链接。

# 验证记录

本文件只记录实际执行的检查，不把编译成功等同于全部功能或设备认证。

## 发布输入核对

- Windows 源码版本：3.3.1。
- macOS 源码版本：3.3.1。
- macOS 来源提交：`d6cf5758928a937b79f535e6fdcd39038bc6e5f8`。
- Windows 安装包：`Lexi-3.3.1-x64-setup.exe`，525,547,528 字节。
- Windows SHA256：`30be541a845f3f42418886a15e9016e3685cdaf250b31f5392fd7607863590d6`。
- macOS DMG：`Lexi-3.3.1-macOS-arm64.dmg`，531,257,599 字节。
- macOS SHA256：`72df2d153e31e6c6a2cc4abd61f9420db04ad1c84d7bdefafd9d86f55642a354`。
- 两端 Git 源树不含嵌套 `.git`、用户数据库、登录文件、私钥或真实客户端配置。

## Windows

- 最终发布树完成 `dotnet publish`，自包含目录生成成功。
- Rust FSRS 助手以 release 模式构建成功，最终 helper 为 2,321,920 字节。
- 安装包资源注入后通过完整 IELTS 校验：`IeltsVerified=True`。
- 打包复制 4,147 个文件，排除 5 个发布边界文件，生成 4,177 条卸载记录。
- `test-packaging.ps1` 全部 PASS。
- 最终发布树的 `--visual123-test`：138 PASS，0 FAIL。
- 最终发布树的 `--learning-integration`：29 PASS，0 FAIL。
- 上述运行使用独立 `LEXI_DATA_DIR`，未读取或改写正在使用的用户数据。
- 已知边界：`--redesign-test` 在宽度 1100 的 exam topics grid placement 断言失败；因此不宣称 Windows 全量 UI 测试通过。
- 最终 EXE 由已发布安装包重新计算 SHA256 并核对为上文记录值。

## macOS

- 从上游提交 `d6cf5758928a937b79f535e6fdcd39038bc6e5f8` 只读同步源码，不修改或推送上游仓库。
- 同步前逐文件比对 4,612 个上游跟踪文件：Missing 0、Extra 0、Changed 0、Hash errors 0。
- 在 LF 规范化后的最终暂存导出树中运行：

```sh
cd macos
python3 -X utf8 -B scripts/verify_source_integrity.py
```

- 源码哈希、UI 契约、59,026 条离线词典、3.3.1 应用身份与卸载门禁均通过。
- 可执行位与上游 Git tree 对齐后复核。
- DMG 为上游 3.3.1 Apple Silicon 发布资产；最终 SHA256 与上文记录值一致。

## 隐私检查

- 扫描 Token、API Key、Cookie、Session、私钥、service role 配置、DPAPI 和真实环境文件。
- 排除个人词库、用户 SQLite、学习记录、计划、日志、缓存和签名材料。
- Windows 打包清理规则及其 `test-packaging.ps1` 检查覆盖用户数据排除项。
- 核对 `social-public.json` 不进入 Git。
- 人工检查 Windows 5 张与 macOS 9 张截图，未发现账号、头像、通知、Cookie、Token 或本机路径。
- 核对 macOS 公开源码保留的 `dictionary.sqlite3`、考试词书和 IELTS 资源与上游公开范围一致，不属于用户词库或学习记录。
- 最终暂存树复核 947 个历史跟踪路径全部恢复，无嵌套 `.git`，安装包与 DMG 仅作为 Release 资产。

## Release 后核对

- 回下载 EXE、DMG 和 `.sha256`。
- 核对资产名、字节数和 SHA256。
- 检查 README 图片、下载链接与 Release 页面。
- 确认提交作者、提交者、标签和 GitHub 上传账户均为 DespairJasper。

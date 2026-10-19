# 参与 Lexi

Lexi 由 Cofran-77 与 DespairJasper 共同署名。欢迎提交缺陷修复、文档、可访问性和平台适配改进。

## 提交前

1. 先搜索已有 Issue，较大功能先说明使用场景、平台和预期行为。
2. 从 `main` 创建短名称分支，一个 PR 聚焦一个问题。
3. 只在相关平台目录修改：`windows/`、`macos/` 或 `android/`。
4. 写清实际运行的构建或测试。编译成功不能替代 UI、安装包和设备验收。
5. UI 缺陷尽量附隔离演示数据截图或最小复现步骤。

## 隐私与资源

- 不提交个人词库、学习记录、计划、账号资料、Cookie、Session、Token、API Key、签名材料、日志或本机配置。
- UI 截图必须使用 `LEXI_DATA_DIR` 等隔离目录，并检查通知、好友资料、路径和密钥。
- Windows 公开 Git 源码不包含未获分发许可的 IELTS 书籍与音频。
- macOS 源码只保留上游公开跟踪的资源；新增第三方内容必须有可核对来源和分发依据。
- 真实 `social-public.json` 不提交，只提交 `social-public.example.json`。

## 许可

- 根目录原创贡献按适用的 [PolyForm Noncommercial 1.0.0](LICENSE) 分发。
- `macos/` 原有内容继续按 [Lexi 非商业许可](macos/LICENSE) 分发。
- 贡献者保留自己的版权；提交商业版本所需的额外授权需另行协商。
- 第三方代码和资料保留各自许可，不能因进入本仓库而重新授权。

## 验证

Windows 最小检查：

```powershell
cargo build --manifest-path windows/native/fsrs-optimizer/Cargo.toml --release --locked
dotnet build windows/Lexi.csproj -c Release
```

macOS 最小检查：

```sh
cd macos
python3 scripts/verify_source_integrity.py
dotnet build lexi_avalonia/Lexi.csproj -c Release
```

涉及安装包、数据迁移、签名、登录或好友同步时，在 PR 中明确列出未验证环境和残余风险。

## 版本与发布

桌面端当前使用统一 Release 标签 `vX.Y.Z-cross-platform`。历史平台标签继续保留。维护者更新 README、更新日志、验证记录和 Release 校验文件后再发布。

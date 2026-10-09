# Lexi for macOS · 协作入口

此目录为 macOS 版本预留，目前没有上传 macOS 源码、资源、安装包或发布标签。

项目作者：**Cofran-77 与 DespairJasper**。macOS 源码由合作作者后续提交；现有参考项目为 [DespairJasper/lexi-macos](https://github.com/DespairJasper/lexi-macos)。该外部仓库的授权以其自身 LICENSE 为准，本仓库当前许可不自动替换外部仓库许可。

## 接入步骤

1. 仓库所有者在 GitHub Settings → Collaborators 邀请合作作者，受邀者接受后再提交。
2. 从 main 创建 `macos/import` 分支，将 Xcode / Swift / 其他实际源码放入本目录；不要上传 DerivedData、私人词库、钥匙串数据、签名证书、私钥或未经授权的教材录音。
3. 保留并核对第三方许可，在本目录补齐构建、运行、签名及数据迁移说明。
4. 提交 Pull Request，由双方核对后合并。正式版本使用 `macos-vX.Y.Z` 标签与独立 Release。

此处不承诺云同步、数据文件兼容、macOS 特定功能已经迁移或安装包已经可用。后续以本端实际实现为准。

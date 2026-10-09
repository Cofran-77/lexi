# 可选资源接入与商业使用边界

## 离线字典

`windows/Assets/dictionary.sqlite3` 与 `android/app/src/main/assets/dictionary.sqlite3` 是从 MIT 许可 ECDICT 生成的 59,026 条常用词子集。保留来源与原作者许可，不能把子集误称为完整 ECDICT。

## IELTS / 自备学习资料

公开版本不含旧 `my-ielts` 资料包、原书音频、图片、PDF 或目录中的翻译与讲义。此前本地完整版本的安装包因此不直接作为公开 Release。

Windows 的目录读取器使用应用输出目录 `Assets/IELTS/catalog.json`；资源路径相对于该目录，解析器阻止路径越界。你可以把自己原创或已获授权的内容放到源码 `windows/Assets/IELTS/`，构建时由现有项目规则复制。

`catalog.example.json` 是本项目为格式说明原创的极小示例，未从教材提取。需要体验时复制为 `catalog.json` 后重新构建；默认不会把示例冒充真实 IELTS 教材。基本结构：

```json
{
  "Source": "My own study notes",
  "Sections": [{
    "Id": "my-section", "Kind": "vocabulary", "Title": "My words",
    "Entries": [{"Id": "my-word", "Words": ["adaptive"], "Meaning": "能够适应变化的", "Group": 1}]
  }],
  "Sentences": []
}
```

具体模型与合法路径检查见 `windows/Application/LearningCatalog.cs`。辅助文件格式按相应资料页面读取逻辑准备，不保证任意第三方格式直接可用。

## 商业尝试

项目原创代码的 MIT 许可允许商用、修改、分发和二次许可，须保留版权与许可声明。商业版本可以维护自己的品牌、签名及分发流程；MIT 不保证产品合规、素材权利或功能质量。任何原书教材、录音、字体、商标或服务商接口条件需独立核对，不能凭项目 LICENSE 推断已经授权。

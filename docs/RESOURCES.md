# 学习资源与接入

## 随应用提供的词典

Windows 与 Android 的 dictionary.sqlite3 来自 ECDICT 常用词子集，包含 59,026 条记录。ECDICT 采用 MIT 许可，其版权与原始许可继续保留；项目的非商业许可不改变这份第三方数据的许可。

## IELTS 资料核对

2026-10-09 核对 hefengxian/my-ielts 的公开 README 与仓库根目录。README 标明“禁止将本项目用于任何商业目的”，并说明内容包括《雅思词汇真经》原书音频、新东方语法讲义、《顾家北手把手教你雅思写作》翻译练习等。根目录未提供可核实的教材权利人再分发授权文件。资料公开可访问不等于允许再分发；仅声明非商业也不能证明转载原书内容不侵权。

因此公开仓库与安装包不复制旧资源包，包含其教材词汇编排、录音、PDF、图片、翻译与讲义。相关工作区源码完整保留，不删除练习实现。后续若取得明确覆盖再分发的授权，再按授权范围接入并记录权利人、来源、许可、文件清单和日期。

## 接入自己的资源

应用读取输出目录 Assets/IELTS/catalog.json。资源路径相对该目录；解析器阻止路径越界。在源码 windows/Assets/IELTS/ 放入自己的原创或获授权数据，现有构建规则会复制到应用输出。单独购买教材一般不自动包含向公众再分发资源的权利。

catalog.example.json 是本项目原创的格式示例，不来自教材。复制为 catalog.json 后可用于检查接入流程；它不代表完整教材。简化结构如下：

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

具体字段和资源读取逻辑见 windows/Application/LearningCatalog.cs。语音、讲义等辅助文件按对应页面的读取格式配置，不保证任意教材格式可以直接导入。

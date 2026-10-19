namespace Lexi;

internal static partial class UiCatalog
{
    static UiCatalog()
    {
        var additions = new Dictionary<string, string>
        {
            ["创建学习计划 ▾"] = "Create study plan ▾",
            ["从词汇档案创建计划..."] = "From your vocabulary…",
            ["从考试专题创建计划..."] = "Create from exam topic…",
            ["暂无学习计划"] = "No study plans yet",
            ["回忆卡测试"] = "Recall",
            ["先学后测模式 · 请仔细阅读释义与例句"] = "Learn first · read the meaning and example",
            ["释义已隐藏，请在心中回忆词义"] = "Recall the meaning before revealing it",
            ["查看释义 ↵"] = "Reveal meaning ↵",
            ["← 返回计划"] = "← Back to plans",
            ["撤销上一次"] = "Undo last rating",
            ["改判为忘记"] = "Change to forgotten",
            ["忘记了 (1)"] = "Forgot (1)",
            ["模糊 (2)"] = "Unsure (2)",
            ["认识 (3)"] = "Known (3)",
            ["朗读 🔊"] = "Listen 🔊",
            ["看完了，开始回忆 → ↵"] = "Start recall → ↵",
            ["返回计划概览"] = "Back to plans",
            ["已完成与已停止"] = "Completed and stopped",
            ["展开 ▾"] = "Expand ▾",
            ["收起 ▴"] = "Collapse ▴",
            ["进度已实时保存至计划"] = "Progress saved",
            ["FSRS 自适应"] = "FSRS adaptive",
            ["按记忆表现动态排期"] = "Scheduled from recall performance",
            ["例句："] = "Example",
            ["计划名称"] = "Plan name",
            ["每日学习词数"] = "Words per day",
            ["学习排期估算"] = "Study estimate",
            ["退出练习"] = "Exit practice",
            ["重启后完整恢复未完成轮次与连击状态。"] = "Unfinished rounds and streaks resume after restart.",
            ["已完成的计划进度会自动保存；切换页面保留本轮连击，重启后完整恢复未完成轮次与连击状态。"] = "Progress is saved. Unfinished rounds and streaks resume after restart.",
            ["已完成 {0} / {1} · 连续认识 {2} / {3}"] = "Completed {0} / {1} · Recognition streak {2} / {3}",
            ["{0} / {1} 页 · 共 {2} 词"] = "Page {0} / {1} · {2} words",
            ["确认删除选中的 {0} 个单词？"] = "Delete the {0} selected words?",
            ["确认删除 '{0}'？"] = "Delete '{0}'?",
            ["最近备份：{0}"] = "Last backup: {0}",
            ["{0}\n包含 {1} 个词。"] = "{0}\nContains {1} words.",
            ["先回忆再揭晓。首次认识即可完成；模糊或忘记后需连续认识三次。"] =
                "Recall first, then reveal. One correct recall completes a word; unsure or forgotten words need three consecutive correct recalls.",
            ["忘记 (← 或 1)"] = "Forgot (← or 1)",
            ["模糊 (↓ 或 2)"] = "Unsure (↓ or 2)",
            ["认识 (→ 或 3)"] = "Known (→ or 3)",
            ["揭晓释义 (Space / Enter)"] = "Reveal meaning (Space / Enter)",
            ["下一词 (Space / Enter)"] = "Next word (Space / Enter)",
            ["撤销 (Ctrl+Z)"] = "Undo (Ctrl+Z)",
            ["复习断点暂时无法恢复，原记录已保留："] = "The review checkpoint could not be restored. The original record was kept: ",
            ["日期已变化，今日复习队列已刷新，请重新回忆。"] = "The study day changed and today's review queue was refreshed. Please recall the word again.",
            ["复习回滚失败："] = "Could not roll back the review: ",
            ["该词已有新的档案或排期变更，本轮已刷新，旧评价不能撤销。"] =
                "This word has a newer archive or schedule change. The round was refreshed and the previous rating can no longer be undone.",
            ["已撤销上一次评价与对应排期变更。"] = "The last rating and its schedule change were undone.",
            ["撤销未完成："] = "Undo incomplete: ",
            ["专注学习"] = "Focus study",
            ["选中词汇专注学习"] = "Focus on selected words",
            ["专注学习 · 已完成 {0}/{1} · 连击 {2}/{3}"] = "Focus study · Completed {0}/{1} · Streak {2}/{3}",
            ["先回忆这个词的含义"] = "Recall what this word means first",
            ["本轮已完成。你可以退出专注。"] = "This round is complete. You can exit focus study.",
            ["学习卡：看过释义后开始回忆。"] = "Learn card: review the meaning, then start recall.",
            ["已记为认识。"] = "Marked as known.",
            ["稍后会再次出现。"] = "It will appear again shortly.",
            ["先自己回忆，再揭晓释义。"] = "Recall the meaning yourself, then reveal it.",
            ["记错了，改为忘记"] = "Change to forgotten",
            ["看完了，开始回忆"] = "Start recall",
            ["揭晓释义"] = "Reveal meaning",
            ["已记录认识。"] = "Known response recorded.",
            ["已记录模糊，将再次出现。"] = "Unsure response recorded. The word will return.",
            ["已记录忘记，将重新学习。"] = "Forgotten response recorded. The word will be relearned.",
            ["已退出专注学习，当前词库进度已保留。"] = "Exited focus study. Your vocabulary progress was kept.",
            ["专注回滚失败："] = "Could not roll back focus study: ",
            ["专注评分失败："] = "Could not save the focus rating: ",
            ["已撤销上一次专注评分。"] = "The last focus rating was undone.",
            ["标记掌握失败："] = "Could not mark the word as mastered: ",
            ["词汇档案为空。"] = "Your vocabulary is empty.",
            ["该词已收藏。"] = "This word is already saved.",
            ["当前词已不在档案中，请从查词页重新查询后收藏。"] =
                "This word is no longer in your vocabulary. Look it up again before saving it.",
            ["当前专注词已收藏。"] = "The current focus word was saved.",
            ["正在测试连接…本次测试会调用所选模型。"] =
                "Testing the connection. This test calls the selected model.",
            ["连接成功 · "] = "Connection succeeded · ",
            ["已取消连接测试。"] = "Connection test canceled.",
            ["连接测试失败："] = "Connection test failed: ",
            ["学习断点保存失败，当前内存进度保留："] =
                "Could not save the learning checkpoint. Current progress remains in memory: ",
            ["学习记录无法读取，原文件已保留："] =
                "Learning progress could not be read. The original file was kept: ",
            ["学习记录保存失败："] = "Could not save learning progress: ",
            ["IELTS 资源加载失败："] = "Could not load IELTS resources: ",
            ["该资源暂不可用。"] = "This resource is currently unavailable.",
            ["资源地址格式无效。"] = "The resource address is invalid.",
            ["未抓取到英文选词，请输入单词查询。支持在其他应用选中单词后按 Alt+D。"] =
                "No English selection was captured. Enter a word, or select one in another app and press Alt+D.",
            ["{0} 个活动计划 · 今日 {1} 词待学"] = "{0} active plans · {1} words to learn today",
            ["已完成 {0} / {1} · 剩余 {2} 词"] = "Completed {0} / {1} · {2} words left",
            ["连击 {0}/{1}"] = "Streak {0}/{1}",
            ["× 隐藏到托盘 · 横线最小化 · 标题栏双击最大化 · 拖动边缘调整大小。完全退出请用托盘菜单。"] =
                "× Hide to the tray · minimize from the title bar · double-click to maximize · drag edges to resize. Quit from the tray menu.",
            ["120 秒"] = "120 seconds",
            ["60 秒"] = "60 seconds",
            ["按服务商支持的协议选择；完整接口地址也可填写。"] =
                "Choose a protocol supported by the provider. A complete endpoint URL is also accepted.",
            ["备份与恢复"] = "Backup & restore",
            ["本地查词不调用 AI。关闭窗口后可通过快捷键或托盘重新打开。"] =
                "Offline lookup never calls AI. Reopen the app from the shortcut or system tray after closing the window.",
            ["测试连接"] = "Test connection",
            ["撤销上一次评价"] = "Undo last rating",
            ["导出…"] = "Export…",
            ["导出当前勾选的词；没有勾选时，导出当前筛选结果。"] =
                "Export selected words, or all filtered results when nothing is selected.",
            ["导入与导出"] = "Import & export",
            ["记住 API Key（使用 Windows 当前用户加密保护）"] =
                "Remember API key (protected by Windows current-user encryption)",
            ["简体中文"] = "Chinese (Simplified)",
            ["接口协议"] = "API protocol",
            ["离线优先 · 缺词按配置 AI 补全 · 扩展需点击生成"] =
                "Offline first · configured AI fills missing words · generate extensions on demand",
            ["切换界面语言"] = "Switch interface language",
            ["取消测试"] = "Cancel test",
            ["收藏到金句本"] = "Save to quotes",
            ["收藏例句到金句本"] = "Save example to quotes",
            ["忘记"] = "Forgot",
            ["选中英文后按 Alt+D 即查；未选中时唤醒输入框。优先使用本地词典，已配置 AI 时可补全未收录的词。"] =
                "Select English text and press Alt+D to look it up. Without a selection, Alt+D focuses the search box. The offline dictionary is used first; configured AI can fill missing words.",
            ["学习统计"] = "Statistics",
            ["已收藏的词汇与学习记录"] = "Saved words and learning history",
            ["最小化到任务栏"] = "Minimize to taskbar",
            ["Chat Completions · 通用兼容"] = "Chat Completions · compatible",
            ["Responses · 流式接口"] = "Responses · streaming API",
        };
        foreach (var pair in additions) English[pair.Key] = pair.Value;
        AddInteractionTranslations();
        foreach (var key in English.Keys.ToList())
            English[key] = English[key].Replace("this Mac", "this PC").Replace("the menu bar", "the system tray");
    }
}

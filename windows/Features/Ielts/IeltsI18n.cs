using System;
using System.Collections.Generic;

namespace Lexi.Features.Ielts;

/// <summary>
/// IELTS 专区动态多语言翻译支持与集中词条。
/// 支持随 UiText.Language 在中英文之间即时响应。
/// </summary>
public static class IeltsI18n
{
    private static readonly Dictionary<string, string> EnTranslations = new(StringComparer.Ordinal)
    {
        ["IELTS 专题"] = "IELTS",
        ["IELTS 学习资料"] = "IELTS Learning Materials",
        ["词汇"] = "Vocabulary",
        ["听力资料"] = "Listening Resources",
        ["阅读同义替换"] = "Synonyms Dictation",
        ["写作练习"] = "100 Sentences Writing",
        ["章节目录"] = "Chapters",
        ["搜索章节..."] = "Search chapters...",
        ["全部章节"] = "All Chapters",
        ["仅词汇"] = "Vocab Only",
        ["仅听力"] = "Listening Only",
        ["搜索当前章节词汇或释义..."] = "Search words or meanings...",
        ["全部"] = "All",
        ["已练习"] = "Practiced",
        ["错误词"] = "Errors",
        ["仅看已选"] = "Selected Only",
        ["查看全部"] = "Show All",
        ["开始练习"] = "Start Practice",
        ["开始学习"] = "Start Learning",
        ["提示拼写"] = "Guided Spelling",
        ["无提示默写"] = "Dictation",
        ["创建每日计划"] = "Create Daily Plan",
        ["同义替换听写"] = "Synonyms Dictation",
        ["练习设置"] = "Practice Settings",
        ["练习数量"] = "Word Count",
        ["全部词"] = "All Words",
        ["随机练习"] = "Random Order",
        ["清空选择"] = "Clear Selection",
        ["清空"] = "Clear",
        ["学习已选词"] = "Practice Selected",
        ["章节录音"] = "Chapter Audio",
        ["播放"] = "Play",
        ["暂停"] = "Pause",
        ["停止"] = "Stop",
        ["当前章节暂无独立音频录音。"] = "No standalone audio file for this chapter.",
        ["无法播放录音，改用系统朗读。"] = "Unable to play recording; falling back to speech synthesis.",
        ["该条目暂无录音。"] = "No recording for this entry.",
        ["同义替换训练已完成。"] = "Synonyms training complete.",
        ["检查全部答案"] = "Check All Answers",
        ["重播读音"] = "Replay Audio",
        ["下一词"] = "Next Word",
        ["全部正确，可以进入下一词。"] = "All correct! Press Next to proceed.",
        ["请正确输入考点词及全部同义替换后继续。"] = "Please enter the target word and all synonyms correctly before continuing.",
        ["返回 IELTS 目录"] = "Back to IELTS Catalog",
        ["显示 / 隐藏答案"] = "Show / Hide Answer",
        ["朗读参考译文"] = "Read Reference Answer",
        ["书中答案："] = "Book Answer: ",
        ["备用译文："] = "Alternate Answer: ",
        ["当前范围没有同义替换练习。"] = "No synonym exercises in the current scope.",
        ["听力与语法学习资料"] = "Listening & Grammar Resources",
        ["课程视频"] = "Course Video",
        ["语法讲义 PDF"] = "Grammar Lecture PDF",
        ["语法思维导图"] = "Grammar Mind Map",
        ["资料来源"] = "Source",
        ["打开完整听力笔记"] = "Open Complete Listening Notes",
        ["100 句翻译练习"] = "100 Sentences Translation Practice",
        ["输入你的英文翻译，自动保存"] = "Type your English translation; auto-saved",
    };

    public static string T(string chinese)
    {
        if (UiText.Language == "en" && EnTranslations.TryGetValue(chinese, out var en))
            return en;
        return chinese;
    }

    public static string SelectedCountFormat(int count, int chapterCount)
    {
        var isEn = UiText.Language == "en";
        if (chapterCount > 1)
        {
            return isEn
                ? $"{count} words selected (across {chapterCount} chapters)"
                : $"已选 {count} 词（跨 {chapterCount} 个章节）";
        }
        return isEn ? $"{count} words selected" : $"已选 {count} 词";
    }

    public static string WordCountFormat(int count)
    {
        return UiText.Language == "en" ? $"{count} words" : $"{count} 词";
    }
}

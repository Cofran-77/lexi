using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Lexi.Features.Ielts;

namespace Lexi;

public partial class MainWindow
{
    private List<LearningWord> _synonymQuestions = [];
    private int _synonymCursor;
    private bool _synonymCorrect;
    private TextBox? _synonymWordInput;
    private TextBox? _synonymAnswersInput;
    private TextBlock? _synonymFeedback;

    private void StartIeltsSynonyms(List<LearningWord> words)
    {
        _synonymQuestions = words.Where(w => w.Synonyms.Count > 0).ToList();
        _synonymCursor = 0;
        if (_synonymQuestions.Count == 0)
        {
            SetStatus(IeltsI18n.T("当前范围没有同义替换练习。"));
            return;
        }

        // 切换到子页面展示
        ShowIeltsSubPage();
        RenderIeltsSynonym();
    }

    private void ShowIeltsSubPage()
    {
        ShowPage("ielts");
        if (_ieltsWorkspace != null) _ieltsWorkspace.IsVisible = false;
        if (_ieltsSubContentPage != null) _ieltsSubContentPage.IsVisible = true;
        _ieltsSubContent?.Children.Clear();
    }

    private void RenderIeltsSynonym()
    {
        if (_ieltsSubContent == null) return;
        _ieltsSubContent.Children.Clear();
        _synonymCorrect = false;

        var body = new StackPanel { Spacing = 14 };

        var topBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = LearningText(IeltsI18n.T("同义替换听写"), 22);
        Grid.SetColumn(title, 0);
        topBar.Children.Add(title);

        var backBtn = LearningButton(IeltsI18n.T("返回 IELTS 目录"), ShowIeltsCatalog);
        Grid.SetColumn(backBtn, 1);
        topBar.Children.Add(backBtn);
        body.Children.Add(topBar);

        if (_synonymCursor >= _synonymQuestions.Count)
        {
            body.Children.Add(LearningText(IeltsI18n.T("同义替换训练已完成。"), 18));
            _ieltsSubContent.Children.Add(LearningCard(body));
            return;
        }

        var word = _synonymQuestions[_synonymCursor];
        body.Children.Add(LearningText($"{IeltsI18n.T("同义替换听写")} {_synonymCursor + 1}/{_synonymQuestions.Count} · {word.Meaning}", 16));

        _synonymWordInput = new TextBox
        {
            Watermark = IeltsI18n.T("听音输入考点词"),
            Name = "SynonymWordInput",
            FontSize = 14
        };
        _synonymAnswersInput = new TextBox
        {
            Watermark = IeltsI18n.T("全部同义词，以逗号分隔"),
            Name = "SynonymAnswersInput",
            AcceptsReturn = true,
            FontSize = 14
        };
        _synonymFeedback = LearningText("");

        body.Children.Add(_synonymWordInput);
        body.Children.Add(_synonymAnswersInput);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(LearningButton(IeltsI18n.T("重播读音"), () => GetLearningAudio().Play(word.Word, IeltsCatalog.ResolveAsset(word.AudioPath))));
        actions.Children.Add(LearningButton(IeltsI18n.T("检查全部答案"), CheckIeltsSynonyms, true));
        actions.Children.Add(LearningButton(IeltsI18n.T("下一词"), () =>
        {
            if (!_synonymCorrect)
            {
                SetStatus(IeltsI18n.T("请正确输入考点词及全部同义替换后继续。"));
                return;
            }
            _synonymCursor++;
            RenderIeltsSynonym();
        }));
        body.Children.Add(actions);

        body.Children.Add(_synonymFeedback);
        _ieltsSubContent.Children.Add(LearningCard(body));

        GetLearningAudio().Play(word.Word, IeltsCatalog.ResolveAsset(word.AudioPath));
    }

    private void CheckIeltsSynonyms()
    {
        if (_synonymCursor >= _synonymQuestions.Count) return;
        var word = _synonymQuestions[_synonymCursor];

        static string Normalize(string value) =>
            string.Join(' ', TypingSession.Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var actual = (_synonymAnswersInput?.Text ?? "")
            .Split([',', '，', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _synonymCorrect = word.Words.Any(w => Normalize(w) == Normalize(_synonymWordInput?.Text ?? "")) &&
                          actual.SetEquals(word.Synonyms.Select(Normalize));

        _synonymFeedback!.Text = _synonymCorrect
            ? IeltsI18n.T("全部正确，可以进入下一词。")
            : IeltsI18n.T("请重新填写：") + word.Word + " · " + string.Join("、", word.Synonyms);

        if (!_synonymCorrect)
            _ieltsProgress.Errors.Add(word.Id);
        else
            _ieltsProgress.Typed.Add(word.Id);

        SaveIeltsProgress();
    }

    private void ShowIeltsResources()
    {
        ShowIeltsSubPage();
        if (_ieltsSubContent == null) return;

        var body = new StackPanel { Name = "IeltsResourcesPanel", Spacing = 14 };

        var topBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = LearningText(IeltsI18n.T("听力与语法学习资料"), 22);
        Grid.SetColumn(title, 0);
        topBar.Children.Add(title);

        var backBtn = LearningButton(IeltsI18n.T("返回 IELTS 目录"), ShowIeltsCatalog);
        Grid.SetColumn(backBtn, 1);
        topBar.Children.Add(backBtn);
        body.Children.Add(topBar);

        // 工具与资料链接（中性操作按钮）
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, path) in new[]
        {
            (IeltsI18n.T("课程视频"), _ieltsCatalog!.GrammarVideo),
            (IeltsI18n.T("语法讲义 PDF"), IeltsCatalog.ResolveAsset("grammar/雅思基础语法配套课程讲义.pdf")),
            (IeltsI18n.T("语法思维导图"), IeltsCatalog.ResolveAsset("grammar/雅思语法.svg")),
            (IeltsI18n.T("资料来源"), _ieltsCatalog.Source)
        })
        {
            var button = LearningButton(label, () => OpenIeltsResource(path));
            button.IsEnabled = !string.IsNullOrEmpty(path);
            tools.Children.Add(button);
        }
        body.Children.Add(tools);

        var listening = _ieltsCatalog.Sections.Where(s => s.Kind == "listening").ToList();
        body.Children.Add(LearningText($"{IeltsI18n.T("听力词汇资料")} · {listening.Sum(s => s.Entries.Count)} 条", 16));

        var sectionLinks = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var section in listening)
        {
            sectionLinks.Children.Add(LearningButton(section.Title, () =>
            {
                SelectIeltsSection(section);
            }));
        }
        body.Children.Add(sectionLinks);

        if (IeltsCatalog.ResolveAsset("listening-notes.txt") is { } notes)
        {
            body.Children.Add(LearningButton(IeltsI18n.T("打开完整听力笔记"), () => OpenIeltsResource(notes)));
            body.Children.Add(new ScrollViewer
            {
                Content = LearningText(File.ReadAllText(notes)),
                MaxHeight = 420
            });
        }

        body.Children.Add(LearningText("资料来自 my-ielts；原作者禁止商业用途。口语和大小作文尚无完整内容。", 12));

        _ieltsSubContent.Children.Add(LearningCard(body));
    }

    private void OpenIeltsResource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) { SetStatus("该资源暂不可用。"); return; }
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile && uri.Scheme is not ("http" or "https"))
        {
            SetStatus("资源地址格式无效。");
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void ShowIeltsWriting()
    {
        ShowIeltsSubPage();
        if (_ieltsSubContent == null) return;

        var body = new StackPanel { Name = "IeltsWritingPanel", Spacing = 14 };

        var topBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = LearningText(IeltsI18n.T("100 句翻译练习"), 22);
        Grid.SetColumn(title, 0);
        topBar.Children.Add(title);

        var backBtn = LearningButton(IeltsI18n.T("返回 IELTS 目录"), ShowIeltsCatalog);
        Grid.SetColumn(backBtn, 1);
        topBar.Children.Add(backBtn);
        body.Children.Add(topBar);

        var rows = new StackPanel { Spacing = 14 };
        foreach (var sentence in _ieltsCatalog!.Sentences)
        {
            var card = new StackPanel { Spacing = 10 };
            card.Children.Add(LearningText($"{sentence.Number:00} · {sentence.Category} · {sentence.Chinese}", 16));

            var draft = new TextBox
            {
                Name = "WritingDraft" + sentence.Number,
                Text = _ieltsProgress.WritingDrafts.GetValueOrDefault(sentence.Number, ""),
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MinHeight = 65,
                Watermark = IeltsI18n.T("输入你的英文翻译，自动保存")
            };
            draft.TextChanged += (_, _) =>
            {
                _ieltsProgress.WritingDrafts[sentence.Number] = draft.Text ?? "";
                SaveIeltsProgress();
            };
            card.Children.Add(draft);

            var answer = LearningText(
                IeltsI18n.T("书中答案：") + sentence.BookAnswer + "\n" +
                IeltsI18n.T("备用译文：") + sentence.AlternateAnswer + "\n" +
                sentence.Remark);
            answer.IsVisible = false;

            var buttonRow = new WrapPanel { Orientation = Orientation.Horizontal };
            buttonRow.Children.Add(LearningButton(IeltsI18n.T("显示 / 隐藏答案"), () => answer.IsVisible = !answer.IsVisible));
            buttonRow.Children.Add(LearningButton(IeltsI18n.T("朗读参考译文"), () => GetLearningAudio().Play(sentence.BookAnswer)));
            card.Children.Add(buttonRow);
            card.Children.Add(answer);

            rows.Children.Add(LearningCard(card));
        }

        body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 620 });
        _ieltsSubContent.Children.Add(LearningCard(body));
    }
}

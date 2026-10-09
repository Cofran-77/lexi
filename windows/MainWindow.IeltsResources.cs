using Avalonia.Controls;
using System.Diagnostics;

namespace Lexi;

public partial class MainWindow
{
    private List<LearningWord> _synonymQuestions = [];
    private int _synonymCursor;
    private bool _synonymCorrect;
    private TextBox? _synonymWordInput, _synonymAnswersInput;
    private TextBlock? _synonymFeedback;
    private void StartIeltsSynonyms(List<LearningWord> words)
    {
        _synonymQuestions = words.Where(w => w.Synonyms.Count > 0).ToList(); _synonymCursor = 0;
        if (_synonymQuestions.Count == 0) { SetStatus("当前范围没有同义替换练习。"); return; }
        RenderIeltsSynonym();
    }
    private void RenderIeltsSynonym()
    {
        _ieltsPageContent!.Children.Clear(); _synonymCorrect = false;
        var body = new StackPanel { Spacing = 12 };
        if (_synonymCursor >= _synonymQuestions.Count) { body.Children.Add(LearningText("同义替换训练已完成。", 20)); _ieltsPageContent.Children.Add(LearningCard(body)); return; }
        var word = _synonymQuestions[_synonymCursor];
        body.Children.Add(LearningText($"同义替换听写 {_synonymCursor + 1}/{_synonymQuestions.Count} · {word.Meaning}", 22));
        _synonymWordInput = new TextBox { Watermark = "听音输入考点词", Name = "SynonymWordInput" };
        _synonymAnswersInput = new TextBox { Watermark = "全部同义词，以逗号分隔", Name = "SynonymAnswersInput", AcceptsReturn = true };
        _synonymFeedback = LearningText(""); body.Children.Add(_synonymWordInput); body.Children.Add(_synonymAnswersInput);
        body.Children.Add(LearningButton("重播读音", () => GetLearningAudio().Play(word.Word, IeltsCatalog.ResolveAsset(word.AudioPath))));
        body.Children.Add(LearningButton("检查全部答案", CheckIeltsSynonyms, true));
        body.Children.Add(LearningButton("下一词", () => { if (!_synonymCorrect) { SetStatus("请正确输入考点词及全部同义替换后继续。"); return; } _synonymCursor++; RenderIeltsSynonym(); }));
        body.Children.Add(_synonymFeedback); _ieltsPageContent.Children.Add(LearningCard(body)); GetLearningAudio().Play(word.Word, IeltsCatalog.ResolveAsset(word.AudioPath));
    }
    private void CheckIeltsSynonyms()
    {
        if (_synonymCursor >= _synonymQuestions.Count) return;
        var word = _synonymQuestions[_synonymCursor];
        static string Normalize(string value) => string.Join(' ', TypingSession.Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var actual = (_synonymAnswersInput?.Text ?? "").Split([',', '，', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(Normalize).ToHashSet();
        _synonymCorrect = word.Words.Any(w => Normalize(w) == Normalize(_synonymWordInput?.Text ?? "")) && actual.SetEquals(word.Synonyms.Select(Normalize));
        _synonymFeedback!.Text = _synonymCorrect ? "全部正确，可以进入下一词。" : "请重新填写：" + word.Word + " · " + string.Join("、", word.Synonyms);
        if (!_synonymCorrect) _ieltsProgress.Errors.Add(word.Id); else _ieltsProgress.Typed.Add(word.Id); SaveIeltsProgress();
    }
    private void ShowIeltsResources()
    {
        _ieltsPageContent!.Children.Clear();
        var body = new StackPanel { Name = "IeltsResourcesPanel", Spacing = 12 };
        body.Children.Add(LearningText("听力与语法学习资料", 22));
        var tools = new WrapPanel();
        foreach (var (label, path) in new[] { ("课程视频", _ieltsCatalog!.GrammarVideo), ("语法讲义 PDF", IeltsCatalog.ResolveAsset("grammar/雅思基础语法配套课程讲义.pdf")), ("语法思维导图", IeltsCatalog.ResolveAsset("grammar/雅思语法.svg")), ("资料来源", _ieltsCatalog.Source) })
        {
            var button = LearningButton(label, () => OpenIeltsResource(path)); button.IsEnabled = !string.IsNullOrEmpty(path); tools.Children.Add(button);
        }
        body.Children.Add(tools);
        var listening = _ieltsCatalog.Sections.Where(s => s.Kind == "listening").ToList();
        body.Children.Add(LearningText($"听力词汇资料 · {listening.Sum(s => s.Entries.Count)} 条", 18));
        foreach (var section in listening)
            body.Children.Add(LearningButton(section.Title, () => { ShowIeltsCatalog(); _ieltsSections!.SelectedItem = section; }));
        if (IeltsCatalog.ResolveAsset("listening-notes.txt") is { } notes)
        {
            body.Children.Add(LearningButton("打开完整听力笔记", () => OpenIeltsResource(notes)));
            body.Children.Add(new ScrollViewer { Content = LearningText(File.ReadAllText(notes)), MaxHeight = 420 });
        }
        body.Children.Add(LearningText("资料来自 my-ielts；原作者禁止商业用途。口语和大小作文尚无完整内容。", 12));
        body.Children.Add(LearningButton("返回 IELTS 目录", ShowIeltsCatalog));
        _ieltsPageContent.Children.Add(LearningCard(body));
    }
    private void OpenIeltsResource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) { SetStatus("该资源暂不可用。"); return; }
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile && uri.Scheme is not ("http" or "https")) { SetStatus("资源地址格式无效。"); return; }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private void ShowIeltsWriting()
    {
        _ieltsPageContent!.Children.Clear();
        var body = new StackPanel { Name = "IeltsWritingPanel", Spacing = 12 };
        body.Children.Add(LearningText("100 句翻译练习", 22));
        body.Children.Add(LearningButton("返回 IELTS 目录", ShowIeltsCatalog));
        var rows = new StackPanel { Spacing = 14 };
        foreach (var sentence in _ieltsCatalog!.Sentences)
        {
            var card = new StackPanel { Spacing = 10 };
            card.Children.Add(LearningText($"{sentence.Number:00} · {sentence.Category} · {sentence.Chinese}", 17));
            var draft = new TextBox { Name = "WritingDraft" + sentence.Number, Text = _ieltsProgress.WritingDrafts.GetValueOrDefault(sentence.Number, ""), AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 65, Watermark = "输入你的英文翻译，自动保存" };
            draft.TextChanged += (_, _) => { _ieltsProgress.WritingDrafts[sentence.Number] = draft.Text ?? ""; SaveIeltsProgress(); };
            card.Children.Add(draft);
            var answer = LearningText("书中答案：" + sentence.BookAnswer + "\n备用译文：" + sentence.AlternateAnswer + "\n" + sentence.Remark); answer.IsVisible = false;
            card.Children.Add(LearningButton("显示 / 隐藏答案", () => answer.IsVisible = !answer.IsVisible));
            card.Children.Add(LearningButton("朗读参考译文", () => GetLearningAudio().Play(sentence.BookAnswer))); card.Children.Add(answer);
            rows.Children.Add(LearningCard(card));
        }
        body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 620 });
        _ieltsPageContent.Children.Add(LearningCard(body));
    }
}

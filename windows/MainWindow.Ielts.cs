using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lexi;

public partial class MainWindow
{
    private IeltsCatalog? _ieltsCatalog;
    private ScrollViewer? _ieltsPage;
    private StackPanel? _ieltsPageContent;
    private Button? _ieltsNav;
    private StackPanel? _ieltsBody;
    private ListBox? _ieltsSections;
    private readonly HashSet<string> _ieltsSelected = [];
    private LearningProgress _ieltsProgress = new();
    private string? _ieltsProgressPath;
    private bool _ieltsProgressBlocked;
    private string _ieltsWordFilter = "all";
    private int _ieltsPracticeCount = 20;
    private bool _ieltsPracticeAll = true;
    private bool _ieltsPracticeRandom;
    private LocalWordAudioPlayer? _learningAudio;
    private LocalWordAudioPlayer GetLearningAudio() => _learningAudio ??= new LocalWordAudioPlayer(SetStatus);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
    private void SystemFeedbackSound(bool correct) => MessageBeep(correct ? 0x40u : 0x30u);
    private void InitializeIelts()
    {
        _ieltsPageContent = new StackPanel { Spacing = 16, Margin = new Thickness(28, 24), MaxWidth = 1040 };
        _ieltsPage = new ScrollViewer
        {
            Name = "IeltsPage", Content = _ieltsPageContent, IsVisible = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        this.FindControl<Panel>("PagesHost")!.Children.Add(_ieltsPage);
        _ieltsNav = new Button { Name = "NavIelts", Content = "IELTS 专题" };
        _ieltsNav.Classes.Add("nav");
        _ieltsNav.HorizontalAlignment = HorizontalAlignment.Stretch;
        _ieltsNav.Click += (_, _) => ShowIeltsCatalog();
        this.FindControl<StackPanel>("LearningNavHost")!.Children.Add(_ieltsNav);
        Closed += (_, _) => { _learningAudio?.Dispose(); StopWindowsLearningAudio(); };
        LoadIeltsProgress();
    }
    private void LoadIeltsProgress()
    {
        _ieltsProgressPath = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "learning-progress.json");
        try { _ieltsProgress = LearningProgress.Load(_ieltsProgressPath); _ieltsProgressBlocked = false; }
        catch (Exception ex) { _ieltsProgress = new(); _ieltsProgressBlocked = true; SetStatus("学习记录无法读取，原文件已保留：" + ex.Message); }
    }
    private void SaveIeltsProgress()
    {
        if (_restoring || !_databaseAvailable || _ieltsProgressBlocked) return;
        var path = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "learning-progress.json");
        if (path != _ieltsProgressPath) { LoadIeltsProgress(); return; }
        try { _ieltsProgress.Save(path); } catch (Exception ex) { SetStatus("学习记录保存失败：" + ex.Message); }
    }
    private void ShowIeltsCatalog()
    {
        ShowPage("ielts");
        _ieltsPageContent!.Children.Clear();
        _ieltsBody = new StackPanel { Spacing = 12 };
        try { _ieltsCatalog ??= IeltsCatalog.Load(); }
        catch (Exception ex) { _ieltsBody.Children.Add(LearningText("IELTS 资源加载失败：" + ex.Message)); _ieltsPageContent.Children.Add(_ieltsBody); return; }
        _ieltsBody.Children.Add(LearningText("IELTS 学习资料", 22));
        _ieltsBody.Children.Add(LearningText("选择章节，再勾选词汇创建计划或开始拼写训练。未勾选时使用本章全部词。"));
        var resourceActions = new WrapPanel();
        resourceActions.Children.Add(LearningButton("听力与语法资料", ShowIeltsResources));
        resourceActions.Children.Add(LearningButton("100 句翻译练习", ShowIeltsWriting));
        _ieltsBody.Children.Add(resourceActions);
        _ieltsSections = new ListBox { ItemsSource = _ieltsCatalog.Sections, MaxHeight = 160 };
        _ieltsSections.SelectionChanged += (_, _) => RenderIeltsSection();
        _ieltsBody.Children.Add(_ieltsSections);
        _ieltsPageContent.Children.Add(_ieltsBody);
        if (_ieltsCatalog.Sections.Count > 0) _ieltsSections.SelectedItem = _ieltsCatalog.Sections.FirstOrDefault(s => s.Id == _ieltsProgress.SelectedSection) ?? _ieltsCatalog.Sections[0];
    }
    private void RenderIeltsSection()
    {
        if (_ieltsBody == null || _ieltsSections?.SelectedItem is not LearningSection section) return;
        while (_ieltsBody.Children.Count > 4) _ieltsBody.Children.RemoveAt(_ieltsBody.Children.Count - 1);
        _ieltsProgress.SelectedSection = section.Id; SaveIeltsProgress();
        _ieltsSelected.Clear();
        _ieltsBody.Children.Add(LearningText(section.Description));
        List<LearningWord> Filtered() => section.Entries.Where(w => (_ieltsSelected.Count == 0 || _ieltsSelected.Contains(w.Id)) && (_ieltsWordFilter == "all" || _ieltsWordFilter == "typed" && _ieltsProgress.Typed.Contains(w.Id) || _ieltsWordFilter == "errors" && _ieltsProgress.Errors.Contains(w.Id))).ToList();
        List<LearningWord> Words() => LearningRound.Select(Filtered(), _ieltsPracticeCount, _ieltsPracticeAll, _ieltsPracticeRandom, Random.Shared.Next());
        var settings = new WrapPanel();
        var count = new TextBox { Text = _ieltsPracticeCount.ToString(), Width = 75, Watermark = "练习数量" };
        count.TextChanged += (_, _) => { if (int.TryParse(count.Text, out var n) && n > 0) _ieltsPracticeCount = n; };
        var all = new CheckBox { Content = "全部词", IsChecked = _ieltsPracticeAll };
        all.IsCheckedChanged += (_, _) => _ieltsPracticeAll = all.IsChecked == true;
        var random = new CheckBox { Content = "随机练习", IsChecked = _ieltsPracticeRandom };
        random.IsCheckedChanged += (_, _) => _ieltsPracticeRandom = random.IsChecked == true;
        settings.Children.Add(count); settings.Children.Add(all); settings.Children.Add(random);
        foreach (var (label, filter) in new[] { ("全部", "all"), ("已练习", "typed"), ("错误词", "errors") }) settings.Children.Add(LearningButton(label, () => { _ieltsWordFilter = filter; RenderIeltsSection(); }));
        _ieltsBody.Children.Add(settings);
        var actions = new WrapPanel();
        actions.Children.Add(LearningButton("有提示拼写", () => StartLearningTyping(Words(), true)));
        actions.Children.Add(LearningButton("无提示默写", () => StartLearningTyping(Words(), false)));
        actions.Children.Add(LearningButton("创建每日计划", () => OpenPlanCreator(DailyStudyPlanSource.Ielts, section), true));
        actions.Children.Add(LearningButton("播放章节录音", () => PlayWindowsLearningAudio(section.AudioPath)));
        actions.Children.Add(LearningButton("停止录音", StopWindowsLearningAudio));
        actions.Children.Add(LearningButton("暂停 / 继续", () => GetLearningAudio().TogglePause()));
        actions.Children.Add(LearningButton("后退 10 秒", () => GetLearningAudio().Seek(GetLearningAudio().Position - 10)));
        actions.Children.Add(LearningButton("前进 10 秒", () => GetLearningAudio().Seek(GetLearningAudio().Position + 10)));
        actions.Children.Add(LearningButton("同义替换听写", () => StartIeltsSynonyms(Filtered())));
        _ieltsBody.Children.Add(actions);
        var list = new StackPanel { Spacing = 6 };
        foreach (var word in Filtered())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var check = new CheckBox { Content = word.Word + " · " + word.Meaning, MaxWidth = 600 };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) _ieltsSelected.Add(word.Id); else _ieltsSelected.Remove(word.Id); };
            row.Children.Add(check); row.Children.Add(LearningButton("朗读", () => PlayWindowsLearningAudio(word.AudioPath))); list.Children.Add(row);
        }
        _ieltsBody.Children.Add(new ScrollViewer { Content = list, MaxHeight = 450 });
    }
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int mciSendString(string command, System.Text.StringBuilder? result, int length, IntPtr callback);
    private void PlayWindowsLearningAudio(string relative)
    {
        var file = IeltsCatalog.ResolveAsset(relative);
        if (file == null) { SetStatus("该条目暂无录音。"); return; }
        GetLearningAudio().Play("", file);
    }
    private void StopWindowsLearningAudio() => _learningAudio?.Stop();
}

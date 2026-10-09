using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lexi;

public partial class MainWindow
{
    private ScrollViewer _learningHubPage = null!;
    private Button _navLearningHub = null!;
    private readonly StackPanel _learningPlansPanel = new() { Spacing = 12 };
    private readonly StackPanel _learningSessionPanel = new() { Spacing = 12 };
    private readonly TextBlock _learningNotice = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _planName = new() { Name = "LearningPlanName", Watermark = "计划名称", Text = "我的学习计划" };
    private readonly TextBox _planQuota = new() { Name = "LearningPlanQuota", Watermark = "每日词数", Text = "20", Width = 120 };
    private readonly CheckBox _planRandom = new() { Name = "LearningPlanRandom", Content = "随机顺序", IsChecked = true };
    private List<DailyStudyPlan> _learningPlans = [];
    private DailyStudyPlanStore? _learningPlanStore;
    private string? _learningPlanPath;
    private bool _learningPlansLoadFailed;
    private DailyStudyPlan? _activeLearningPlan;
    private DailyStudyPlanSession? _dailyLearningSession;
    private DateOnly _dailyLearningDate;
    private bool _planAnswerVisible;
    private TypingSession? _learningTypingSession;
    private TextBox? _learningTypingInput;
    private StackPanel? _learningTypingLetters;
    private TextBlock? _learningTypingResult;
    private TextBlock? _learningTypingStats;
    private List<LearningWord> _lastTypingWords = [];
    private Border? _typingHost;
    private string _typingPreviousPage = "learning";

    private void InitializeLearningHub()
    {
        var body = new StackPanel { Spacing = 20, Margin = new Thickness(28, 24), MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(LearningText("学习计划", 26));
        var create = new WrapPanel { Orientation = Orientation.Horizontal };
        create.Children.Add(LearningButton("创建词汇档案计划", () => OpenPlanCreator(DailyStudyPlanSource.Archive), true));
        create.Children.Add(LearningButton("创建 IELTS 计划", () => OpenPlanCreator(DailyStudyPlanSource.Ielts)));
        body.Children.Add(create);
        body.Children.Add(_learningNotice);
        body.Children.Add(_learningSessionPanel); body.Children.Add(_learningPlansPanel);
        _learningHubPage = new ScrollViewer { Name = "LearningHubPage", Content = body, IsVisible = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        this.FindControl<Panel>("PagesHost")!.Children.Add(_learningHubPage);
        _navLearningHub = new Button { Name = "NavLearningHub", Content = "学习计划", HorizontalAlignment = HorizontalAlignment.Stretch };
        _navLearningHub.Classes.Add("nav");
        _navLearningHub.Click += (_, _) => ShowPage("learning");
        this.FindControl<StackPanel>("LearningNavHost")!.Children.Add(_navLearningHub);
        _typingHost = new Border { IsVisible = false };
        Grid.SetRowSpan(_typingHost, 3);
        _typingHost.SetValue(Panel.ZIndexProperty, 100);
        ((Grid)RootWindowBorder.Child!).Children.Add(_typingHost);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (_typingHost.IsVisible && e.Key == Key.Escape)
            {
                ExitLearningTyping(); e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ReloadLearningPlans();
    }

    private static TextBlock LearningText(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static Border LearningCard(Control child)
    {
        var card = new Border { Child = child, Padding = new Thickness(18) }; card.Classes.Add("card"); return card;
    }
    private Button LearningButton(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(14, 9) };
        button.Classes.Add(primary ? "primary" : "secondary");
        button.Click += (_, _) =>
        {
            if (_restoring || !_databaseAvailable) { SetStatus("词库正在恢复或不可用，请稍后再试。"); return; }
            try { action(); } catch (Exception ex) { SetStatus("学习操作失败：" + ex.Message); }
        };
        return button;
    }

    private void ShowLearningHub(bool visible)
    {
        if (_learningHubPage == null) return;
        _learningHubPage.IsVisible = visible;
        if (_typingHost != null) _typingHost.IsVisible = visible && _learningTypingSession != null;
        _navLearningHub.Classes.Set("active", visible);
        if (!visible) return;
        var path = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "daily-plans.json");
        if (!string.Equals(path, _learningPlanPath, StringComparison.OrdinalIgnoreCase)) ReloadLearningPlans();
        RenderLearningPlans();
        if (_learningTypingSession == null) RenderDailyLearning();
    }

    // Called after restoring the vocabulary database, including restores at the same path.
    private void ReloadLearningPlans()
    {
        LoadIeltsProgress();
        _activeLearningPlan = null; _dailyLearningSession = null; _learningTypingSession = null;
        if (_typingHost != null) _typingHost.IsVisible = false;
        _learningSessionPanel.Children.Clear();
        _learningPlanPath = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "daily-plans.json");
        _learningPlanStore = new DailyStudyPlanStore(_learningPlanPath);
        try { _learningPlans = _learningPlanStore.Load(); _learningPlansLoadFailed = false; _learningNotice.Text = "已完成的计划词汇会自动保存；切换页面保留本轮连击，重启后未完成词重新学习。"; }
        catch (Exception ex)
        {
            _learningPlans = []; _learningPlansLoadFailed = true;
            _learningNotice.Text = "无法读取学习计划，已保留原文件且暂停计划写入。请备份并修复 " + _learningPlanPath + "。原因：" + ex.Message;
            SetStatus("计划文件读取失败，原文件未覆盖。");
        }
        RenderLearningPlans();
    }

    private bool SaveLearningPlans(IReadOnlyList<DailyStudyPlan> plans)
    {
        if (_restoring || !_databaseAvailable || _learningPlansLoadFailed || _learningPlanStore == null) { SetStatus("当前不能保存计划，请先解决词库或计划文件的问题。"); return false; }
        if (!ReplayMemoryJournal()) return false;
        var currentPath = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "daily-plans.json");
        if (!string.Equals(currentPath, _learningPlanPath, StringComparison.OrdinalIgnoreCase)) { ReloadLearningPlans(); SetStatus("词库位置已改变，请重新选择计划。"); return false; }
        try { _learningPlanStore.Save(plans); return true; }
        catch (Exception ex) { SetStatus("计划保存失败，操作已撤回：" + ex.Message); return false; }
    }

    private List<DailyStudyPlanWord> ArchivePlanWords()
    {
        var selected = _allWords.Where(w => w.Selected).ToList();
        return (selected.Count > 0 ? selected : _allWords).Select(w => new DailyStudyPlanWord
        {
            Id = w.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Word = w.Word,
            Meaning = w.Translation, Phonetic = w.Phonetic, Definition = w.Definition,
            Example = string.Join("\n", w.AiExamples.Select(e => e.English + "\n" + e.Chinese))
        }).ToList();
    }

    private void CreateLearningPlan()
    {
        if (!int.TryParse(_planQuota.Text, out var quota) || quota is < 1 or > 10000) { SetStatus("每日词数请输入 1–10000 的整数。"); return; }
        var words = ArchivePlanWords();
        if (words.Count == 0) { SetStatus("词汇档案为空，请先收藏或导入词汇。"); return; }
        var plan = DailyStudyPlanRules.Create(_planName.Text ?? "", DailyStudyPlanSource.Archive, "词汇档案", words, quota, _planRandom.IsChecked == true, Random.Shared.Next());
        var overlaps = DailyStudyPlanRules.FindOverlaps(_learningPlans, plan);
        var updated = _learningPlans.Append(plan).ToList();
        if (!SaveLearningPlans(updated)) return;
        _learningPlans = updated; RenderLearningPlans();
        SetStatus(overlaps.Count > 0 ? $"计划已创建；与其他进行中计划有 {overlaps.Select(o => o.Word).Distinct().Count()} 个重复词，进度分别记录。" : $"计划已创建，共 {words.Count} 个词。");
    }

    private void ReplaceLearningPlan(DailyStudyPlan previous, DailyStudyPlan replacement)
    {
        var updated = _learningPlans.Select(p => p.Id == previous.Id ? replacement : p).ToList();
        if (!SaveLearningPlans(updated)) return;
        _learningPlans = updated;
        if (_activeLearningPlan?.Id == previous.Id) { _activeLearningPlan = null; _dailyLearningSession = null; _learningSessionPanel.Children.Clear(); }
        RenderLearningPlans(); SetStatus("计划已保存，已完成进度保留。");
    }

    private DailyStudyPlan CloneLearningPlan(DailyStudyPlan plan) => System.Text.Json.JsonSerializer.Deserialize<DailyStudyPlan>(System.Text.Json.JsonSerializer.Serialize(plan))!;

    private void RenderLearningPlans()
    {
        _learningPlansPanel.Children.Clear();
        var active = _learningPlans.Where(p => p.Status == DailyStudyPlanStatus.Active).ToList();
        var today = active.Sum(p => DailyStudyPlanRules.GetTodayWords(p, DateOnly.FromDateTime(DateTime.Now)).Count);
        _learningPlansPanel.Children.Add(LearningText($"{active.Count} 个活动计划 · 今日 {today} 词待学", 14));
        if (_learningPlans.Count == 0) _learningPlansPanel.Children.Add(LearningText(_learningPlansLoadFailed ? "计划文件暂不可用。" : "暂无活动计划。"));
        if (_learningPlansLoadFailed) _learningPlansPanel.Children.Add(LearningButton("修复后重新读取计划文件", ReloadLearningPlans));
        foreach (var group in new[] { ("IELTS 专题", _learningPlans.Where(p => p.Status == DailyStudyPlanStatus.Active && p.Source == DailyStudyPlanSource.Ielts)),
                                      ("词汇档案", _learningPlans.Where(p => p.Status == DailyStudyPlanStatus.Active && p.Source == DailyStudyPlanSource.Archive)),
                                      ("已完成与已停止", _learningPlans.Where(p => p.Status != DailyStudyPlanStatus.Active)) })
        {
            var plans = group.Item2.ToList();
            _learningPlansPanel.Children.Add(LearningText(group.Item1, 19));
            if (plans.Count == 0) { _learningPlansPanel.Children.Add(LearningText(group.Item1 == "已完成与已停止" ? "暂无记录。" : "暂无活动计划。")); continue; }
            foreach (var plan in plans)
            {
            var panel = new StackPanel { Spacing = 10 };
            var status = plan.Status switch { DailyStudyPlanStatus.Active => "进行中", DailyStudyPlanStatus.Stopped => "已停止", _ => "已完成" };
            panel.Children.Add(LearningText($"{plan.Name} · {status}", 18));
            panel.Children.Add(LearningText($"已完成 {plan.CompletedWordIds.Count}/{plan.Words.Count} · 每日 {plan.DailyWordCount} 词 · 预计剩余 {DailyStudyPlanRules.EstimatedDaysRemaining(plan)} 天"));
            var actions = new WrapPanel { Orientation = Orientation.Horizontal };
            if (plan.Status == DailyStudyPlanStatus.Active)
            {
                actions.Children.Add(LearningButton("开始 / 继续今日学习", () => StartDailyLearning(plan), true));
                actions.Children.Add(LearningButton("停止计划", () => SetLearningPlanStopped(plan)));
                var name = new TextBox { Text = plan.Name, Watermark = "计划名称" };
                var quota = new TextBox { Text = plan.DailyWordCount.ToString(), Width = 100 };
                var random = new CheckBox { Content = "未来批次随机", IsChecked = plan.RandomOrder };
                var editor = new StackPanel { Spacing = 10, IsVisible = false };
                editor.Children.Add(name);
                var adjust = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                adjust.Children.Add(quota); adjust.Children.Add(random);
                adjust.Children.Add(LearningButton("保存调整", () =>
                {
                    if (!int.TryParse(quota.Text, out var count) || count is < 1 or > 10000) { SetStatus("每日词数请输入 1–10000 的整数。"); return; }
                    AdjustLearningPlan(plan, name.Text ?? "", count, random.IsChecked == true);
                }));
                editor.Children.Add(adjust);
                editor.Children.Add(LearningText("调整在未来批次生效，已完成词保留。", 12));
                panel.Children.Add(editor);
                actions.Children.Add(LearningButton("调整", () => editor.IsVisible = !editor.IsVisible));
            }
            else
            {
                if (plan.Status == DailyStudyPlanStatus.Stopped) actions.Children.Add(LearningButton("恢复计划", () => ResumeLearningPlan(plan)));
                actions.Children.Add(LearningButton("删除计划", () =>
                {
                    // A second explicit click makes removal reviewable without a modal window.
                    panel.Children.Add(LearningText("删除只清除此计划记录，词汇档案不受影响。"));
                    panel.Children.Add(LearningButton("确认删除此计划", () =>
                    {
                        var updated = _learningPlans.Where(p => p.Id != plan.Id).ToList();
                        if (!SaveLearningPlans(updated)) return;
                        _learningPlans = updated;
                        if (_activeLearningPlan?.Id == plan.Id) { _activeLearningPlan = null; _dailyLearningSession = null; _learningSessionPanel.Children.Clear(); }
                        RenderLearningPlans(); SetStatus("计划已删除。");
                    }));
                }));
            }
            if (plan.CurrentBatchWordIds.Count > 0)
                actions.Children.Add(LearningButton("最近批次拼写 / 默写", () => ShowLastPlanBatch(plan)));
            panel.Children.Add(actions); _learningPlansPanel.Children.Add(LearningCard(panel));
            }
        }
    }

    private void SetLearningPlanStopped(DailyStudyPlan plan)
    {
        var copy = CloneLearningPlan(plan); DailyStudyPlanRules.Stop(copy); ReplaceLearningPlan(plan, copy);
    }
    private void ResumeLearningPlan(DailyStudyPlan plan)
    {
        var copy = CloneLearningPlan(plan); DailyStudyPlanRules.Resume(copy); ReplaceLearningPlan(plan, copy);
    }
    private void AdjustLearningPlan(DailyStudyPlan plan, string name, int quota, bool random) =>
        ReplaceLearningPlan(plan, DailyStudyPlanRules.Adjust(plan, name, quota, random, Random.Shared.Next()));
    private void ShowLastPlanBatch(DailyStudyPlan plan)
    {
        _learningTypingSession = null; _learningSessionPanel.Children.Clear();
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(LearningText(plan.Name + " · 最近批次训练", 20));
        var words = plan.CurrentBatchWordIds.Select(id => plan.Words.Single(w => w.Id == id)).ToList();
        AddBatchTypingButtons(panel, "本批全部词", words);
        AddBatchTypingButtons(panel, "本批曾忘记词", words.Where(w => plan.ForgotWordIds.Contains(w.Id)).ToList());
        _learningSessionPanel.Children.Add(LearningCard(panel));
    }

    private void StartDailyLearning(DailyStudyPlan plan)
    {
        if (plan.Status != DailyStudyPlanStatus.Active) { SetStatus("该计划已停止或完成。"); return; }
        _learningTypingSession = null;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_activeLearningPlan?.Id != plan.Id || _dailyLearningSession == null || _dailyLearningDate != today)
        {
            if (DailyStudyPlanRules.GetTodayWords(plan, today).Count == 0) { SetStatus("今日计划已完成，请明天继续；可在词汇档案开始拼写训练。"); return; }
            var copy = CloneLearningPlan(plan);
            var session = new DailyStudyPlanSession(copy, today);
            var updated = _learningPlans.Select(p => p.Id == copy.Id ? copy : p).ToList();
            if (!SaveLearningPlans(updated)) return;
            _learningPlans = updated; _activeLearningPlan = copy; _dailyLearningSession = session; _dailyLearningDate = today;
            BeginPlanMemory(copy, session);
        }
        _planAnswerVisible = false; RenderDailyLearning(); RenderLearningPlans();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _learningSessionPanel.BringIntoView());
    }

    private void RenderDailyLearning()
    {
        _learningSessionPanel.Children.Clear();
        if (_dailyLearningSession == null || _activeLearningPlan == null) return;
        var session = _dailyLearningSession; var round = session.Round;
        var card = new StackPanel { Spacing = 12 };
        card.Children.Add(LearningText($"{_activeLearningPlan.Name} · 今日学习", 20));
        card.Children.Add(LearningText($"本批已完成 {round.Completed}/{round.Total} · 剩余 {round.Remaining} 个词"));
        var undo = LearningButton("撤销上一次评价", UndoDailyLearning);
        undo.IsEnabled = session.CanUndo; card.Children.Add(undo);
        if (round.IsFinished)
        {
            card.Children.Add(LearningText("今日批次完成！每个词已连续认识三次。可选择本批词汇继续拼写训练。", 18));
            var all = session.BatchWords.ToList();
            AddBatchTypingButtons(card, "本批全部词", all);
            AddBatchTypingButtons(card, "本批曾忘记词", all.Where(w => _activeLearningPlan.ForgotWordIds.Contains(w.Id)).ToList());
        }
        else
        {
            var word = _activeLearningPlan.Words.Single(w => w.Id == round.Current);
            PresentPlanMemory(_activeLearningPlan, round.Current, round.CurrentStep);
            card.Children.Add(LearningText(word.Word, 32));
            card.Children.Add(LearningText(word.Phonetic));
            card.Children.Add(LearningButton("朗读单词", () => GetLearningAudio().Play(word.Word, IeltsCatalog.ResolveAsset(word.AudioPath))));
            var learn = round.CurrentStep == StudyStep.Learn;
            card.Children.Add(LearningText(learn ? "先看释义，再进入回忆；连续三次认识才完成。" : $"回忆卡 · 连击 {round.CurrentStreak}/{round.CurrentTarget}"));
            if (learn || _planAnswerVisible)
            {
                card.Children.Add(LearningText(word.Meaning, 20));
                if (!string.IsNullOrWhiteSpace(word.Definition)) card.Children.Add(LearningText(word.Definition));
                if (!string.IsNullOrWhiteSpace(word.Example)) card.Children.Add(LearningText(word.Example));
            }
            if (learn) card.Children.Add(LearningButton("看完了，开始回忆", CompleteDailyLearn, true));
            else if (!_planAnswerVisible) card.Children.Add(LearningButton("揭晓释义", () => { _planAnswerVisible = true; RenderDailyLearning(); }, true));
            else
            {
                var ratings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                foreach (var (label, rating) in new[] { ("认识", StudyRating.Known), ("模糊", StudyRating.Unsure), ("忘记", StudyRating.Forgot) })
                    ratings.Children.Add(LearningButton(label, () => RateDailyLearning(rating), rating == StudyRating.Known));
                card.Children.Add(ratings);
            }
        }
        _learningSessionPanel.Children.Add(LearningCard(card));
    }

    private void CompleteDailyLearn()
    {
        if (_restoring || !_databaseAvailable || _dailyLearningSession == null) return;
        _dailyLearningSession.CompleteLearn(); _planPresentation = null; _planAnswerVisible = false; RenderDailyLearning();
    }
    private void RateDailyLearning(StudyRating rating)
    {
        if (_restoring || !_databaseAvailable || _dailyLearningSession == null) return;
        if (_dailyLearningDate != DateOnly.FromDateTime(DateTime.Now) && _activeLearningPlan != null)
        { StartDailyLearning(_activeLearningPlan); SetStatus("日期已变化，今日计划卡片已刷新，请重新回忆。"); return; }
        _planStreakBefore = _dailyLearningSession.Round.CurrentStreak;
        _planRatingApplied = false;
        if (_dailyLearningSession.Rate(rating, () => SaveLearningPlans(_learningPlans)) == null) return;
        _planAnswerVisible = false; RenderDailyLearning(); RenderLearningPlans();
    }
    private void UndoDailyLearning()
    {
        if (_restoring || !_databaseAvailable || _dailyLearningSession == null) return;
        if (_dailyLearningSession.Undo(() => SaveLearningPlans(_learningPlans)))
        { UndoPlanMemory(); _planAnswerVisible = false; RenderDailyLearning(); RenderLearningPlans(); }
    }

    private static LearningWord ToTypingWord(DailyStudyPlanWord word) => new() { Id = word.Id, Words = [word.Word], Meaning = word.Meaning, Phonetic = word.Phonetic, Example = word.Example, AudioPath = word.AudioPath };
    private void AddBatchTypingButtons(StackPanel panel, string label, List<DailyStudyPlanWord> words)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(LearningText($"{label} · {words.Count} 词"));
        foreach (var hints in new[] { true, false })
        {
            var button = LearningButton(hints ? "有提示拼写" : "无提示默写", () => StartLearningTyping(words.Select(ToTypingWord).ToList(), hints));
            button.IsEnabled = words.Count > 0; row.Children.Add(button);
        }
        panel.Children.Add(row);
    }
    private void StartArchiveTyping(bool hints) => StartLearningTyping(ArchivePlanWords().Select(ToTypingWord).ToList(), hints);
    private void StartLearningTyping(List<LearningWord> words, bool hints)
    {
        if (words.Count == 0) { SetStatus("没有可训练的词汇，请先收藏或勾选词汇。"); return; }
        _typingPreviousPage = _currentPage;
        if (_currentPage != "learning") ShowPage("learning");
        _lastTypingWords = words; _learningTypingSession = new TypingSession(); _learningTypingSession.Reset(words, hints); RenderLearningTyping();
        _typingHost!.IsVisible = true;
    }

    private void ExitLearningTyping()
    {
        _learningTypingSession = null;
        _typingHost!.IsVisible = false;
        RenderDailyLearning();
        if (_typingPreviousPage != "learning") ShowPage(_typingPreviousPage);
    }

    private void RenderLearningTyping()
    {
        var session = _learningTypingSession; if (session == null) return;
        _learningSessionPanel.Children.Clear();
        var surface = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#C6E7F3")),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(28),
            MinHeight = Math.Max(450, Bounds.Height - 185)
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 20 };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        top.Children.Add(LearningText($"{session.Cursor}/{session.Count}", 15));
        var mode = LearningText(session.Hints ? "淡写" : "默写", 14);
        Grid.SetColumn(mode, 2); top.Children.Add(mode);
        Grid.SetRow(top, 0); layout.Children.Add(top);

        var center = new StackPanel
        {
            Spacing = 20, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 670
        };
        _learningTypingStats = LearningText("");
        _learningTypingStats.FontSize = 12;
        _learningTypingStats.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(_learningTypingStats, 1);
        top.Children.Add(_learningTypingStats);
        if (session.Current == null)
        {
            center.Children.Add(LearningText("本轮完成", 30));
            center.Children.Add(LearningText($"练习 {session.Count} 词 · 错误词 {session.ErrorIds.Count} · 重试 {session.Retries} 次", 17));
            var errors = _lastTypingWords.Where(w => session.ErrorIds.Contains(w.Id)).ToList();
            if (errors.Count > 0)
            {
                center.Children.Add(LearningText("本次错误词：" + string.Join("、", errors.Select(w => w.Word))));
                center.Children.Add(LearningButton("再练错误词", () => StartLearningTyping(errors, session.Hints), true));
            }
        }
        else
        {
            var word = session.Current;
            if (session.Hints)
            {
                var prompt = LearningText(word.Word, Math.Max(27, 54 - Math.Max(0, word.Word.Length - 14) * 1.5));
                prompt.FontFamily = new FontFamily("Consolas, Menlo, monospace");
                prompt.Foreground = new SolidColorBrush(Color.Parse("#829BB0"));
                prompt.HorizontalAlignment = HorizontalAlignment.Center;
                center.Children.Add(prompt);
            }
            else center.Children.Add(LearningText($"{TypingSession.Normalize(word.Word).Length} 个字母", 14));
            var meaning = LearningText(string.IsNullOrWhiteSpace(word.Meaning) ? "该词暂无释义" : word.Meaning, 22);
            meaning.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(meaning);
            _learningTypingLetters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            center.Children.Add(_learningTypingLetters);
            _learningTypingInput = new TextBox
            {
                Name = "LearningTypingInput", Watermark = "输入英文", FontSize = 30,
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                Width = 490, MaxWidth = 490, HorizontalContentAlignment = HorizontalAlignment.Center
            };
            _learningTypingInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; SubmitLearningTyping(); } };
            _learningTypingInput.TextChanged += (_, _) => RenderTypingLetters(false);
            center.Children.Add(_learningTypingInput);
            _learningTypingResult = LearningText("");
            _learningTypingResult.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(_learningTypingResult);
        }
        Grid.SetRow(center, 1); layout.Children.Add(center);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        bottom.Children.Add(LearningButton("退出练习", ExitLearningTyping));
        var centerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        if (session.Current is { } current)
        {
            centerActions.Children.Add(LearningButton("朗读", () => GetLearningAudio().Play(current.Word, IeltsCatalog.ResolveAsset(current.AudioPath))));
            if (!string.IsNullOrWhiteSpace(current.Example))
                centerActions.Children.Add(LearningButton("例句", () => GetLearningAudio().Play(current.Example)));
        }
        Grid.SetColumn(centerActions, 1); bottom.Children.Add(centerActions);
        if (session.Current != null)
        {
            var submit = LearningButton("确认 ↵", SubmitLearningTyping, true);
            Grid.SetColumn(submit, 2); bottom.Children.Add(submit);
        }
        Grid.SetRow(bottom, 2); layout.Children.Add(bottom);
        surface.Child = layout;
        _typingHost!.Child = surface;
        _typingHost.IsVisible = true;
        UpdateTypingStats();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_learningTypingSession == session) _learningTypingInput?.Focus(); });
    }

    private void SubmitLearningTyping()
    {
        if (_restoring || !_databaseAvailable) { SetStatus("词库正在恢复或不可用。"); return; }
        var session = _learningTypingSession; if (session?.Current == null || _learningTypingInput == null) return;
        if (session.Outcome == TypingOutcome.Correct) { session.Advance(); RenderLearningTyping(); return; }
        var outcome = session.Submit(_learningTypingInput.Text ?? "");
        if (outcome == TypingOutcome.Correct) { _ieltsProgress.Typed.Add(session.Current.Id); SaveIeltsProgress(); }
        else if (outcome == TypingOutcome.Retry) { _ieltsProgress.Errors.Add(session.Current.Id); SaveIeltsProgress(); }
        RenderTypingLetters(true); UpdateTypingStats();
        if (outcome == TypingOutcome.Retry) { _learningTypingResult!.Text = "有错字，请重新输入。红色为错字，绿色为正确字母。"; _learningTypingInput.SelectAll(); SystemFeedbackSound(false); }
        else if (outcome == TypingOutcome.Correct) { _learningTypingResult!.Text = "拼写正确！按 Enter 进入下一词。"; _learningTypingInput.IsReadOnly = true; SystemFeedbackSound(true); }
        else _learningTypingResult!.Text = "输入尚不完整，请继续输入。";
    }
    private void UpdateTypingStats()
    {
        var s = _learningTypingSession;
        if (s != null && _learningTypingStats != null) _learningTypingStats.Text = $"完成 {s.Cursor}/{s.Count} · 字符准确率 {s.Accuracy:F1}% · 重试 {s.Retries} 次 · {s.Wpm} WPM";
    }
    private void RenderTypingLetters(bool submitted)
    {
        var s = _learningTypingSession;
        if (s?.Current == null || _learningTypingLetters == null || _learningTypingInput == null) return;
        _learningTypingLetters.Children.Clear();
        var input = TypingSession.Normalize(_learningTypingInput.Text ?? "");
        // Dictation must not expose per-letter grading before an accepted full-length submission.
        if (!s.Hints && (!submitted || s.Outcome == TypingOutcome.Pending))
        {
            foreach (var ch in input) _learningTypingLetters.Children.Add(LearningText(ch.ToString(), 23));
            return;
        }
        foreach (var letter in TypingFeedbackModel.Build(TypingSession.Normalize(s.Current.Word), input, s.Hints, s.Outcome))
        {
            var text = LearningText(letter.Character.ToString(), 23);
            text.Foreground = letter.Tone switch { TypingLetterTone.Correct => new SolidColorBrush(Color.Parse("#39835B")), TypingLetterTone.Wrong => Brushes.IndianRed, _ => Brushes.Gray };
            _learningTypingLetters.Children.Add(text);
        }
    }
}

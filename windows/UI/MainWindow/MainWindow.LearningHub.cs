using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lexi.Controls;
using Lexi.Features.Learning;
using Lexi.Social;

namespace Lexi;

public partial class MainWindow
{
    private Grid _learningHubPage = null!;
    private Button _navLearningHub = null!;
    private ScrollViewer _managementHost = null!;
    private Grid _studyWorkspaceHost = null!;
    private Control? _learningManagementOverview;

    private readonly StackPanel _learningPlansPanel = new() { Spacing = 16 };
    private readonly TextBlock _learningNotice = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 13 };
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
    private bool _planRatedPending;
    private string? _planRatedWordId;
    private bool _planRatedWordCompleted;
    private bool _completedPlansExpanded = true;
    private bool _stoppedPlansExpanded;
    private string _learningPlanFilter = "all";
    private string? _learningPlanDetailId;
    private readonly List<LearningPlanQueueEntry> _learningPlanQueue = [];
    private int _learningPlanQueueIndex = -1;
    private sealed record LearningPlanQueueEntry(string PlanId, StudyMode Mode, DateOnly LearningDate);

    // 拼写练习宿主与状态
    private TypingSession? _learningTypingSession;
    private TextBox? _learningTypingInput;
    private StackPanel? _learningTypingLetters;
    private TextBlock? _learningTypingResult;
    private TextBlock? _learningTypingStats;
    private SelectableTextBlock? _learningTypingHint;
    private List<LearningWord> _lastTypingWords = [];
    private Border? _typingHost;
    private CancellationTokenSource? _typingAutoAdvanceCts;
    private bool _learningTypingAnswerVisible;
    private bool _learningTypingPhoneticVisible;
    private string _typingPreviousPage = "learning";
    private bool _typingReturnToPlanDetail;
    private string? _typingReturnPlanId;
    // 只有 IELTS 专题入口的拼写才回写全局 IELTS 进度，档案/计划/考试词的拼写不得串档。
    private bool _learningTypingIeltsScope;

    private void InitializeLearningHub()
    {
        // 1. 管理概览视图（内部独立滚动）
        var managementBody = new StackPanel
        {
            Spacing = 18,
            Margin = new Thickness(28, 24),
            MaxWidth = 1040,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        titleRow.Children.Add(LearningText("学习计划", 26));

        // 创建计划收敛为唯一实心主动作按钮 + 来源菜单
        var createBtn = new Button
        {
            Content = UiText.Text("创建学习计划 ▾"),
            Padding = new Thickness(16, 9)
        };
        createBtn.Classes.Add("primary");

        var menuFlyout = new MenuFlyout();
        var itemArchive = new MenuItem { Header = UiText.Text("从词汇档案创建计划...") };
        itemArchive.Click += (_, _) => OpenPlanCreator(DailyStudyPlanSource.Archive);
        var itemIelts = new MenuItem { Header = UiText.Text("从考试专题创建计划...") };
        itemIelts.Click += (_, _) => OpenPlanCreator(DailyStudyPlanSource.Ielts);

        menuFlyout.Items.Add(itemArchive);
        menuFlyout.Items.Add(itemIelts);
        createBtn.Flyout = menuFlyout;

        Grid.SetColumn(createBtn, 1);
        titleRow.Children.Add(createBtn);
        managementBody.Children.Add(titleRow);

        managementBody.Children.Add(_learningNotice);
        managementBody.Children.Add(_learningPlansPanel);

        _learningManagementOverview = managementBody;

        _managementHost = new ScrollViewer
        {
            Name = "LearningManagementHost",
            Content = managementBody,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
        };

        // 2. 独立专注学习工作区（覆盖管理区，固定页头/底部评分，仅内容滚动）
        _studyWorkspaceHost = new Grid
        {
            Name = "StudyWorkspaceHost",
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            IsVisible = false
        };
        _studyWorkspaceHost.Bind(Panel.BackgroundProperty, this.GetResourceObservable("StudyGradientBrush"));

        // 3. 管理页留在 PagesHost；学习工作区提升到窗口正文层，覆盖侧栏但不遮标题栏。
        _learningHubPage = new Grid { Name = "LearningHubPage", IsVisible = false };
        _learningHubPage.Children.Add(_managementHost);

        this.FindControl<Panel>("PagesHost")!.Children.Add(_learningHubPage);
        var mainGrid = (Grid)((Grid)RootWindowBorder.Child!).Children[0];
        Grid.SetRow(_studyWorkspaceHost, 1);
        _studyWorkspaceHost.SetValue(Panel.ZIndexProperty, 90);
        mainGrid.Children.Add(_studyWorkspaceHost);

        // 导航项
        _navLearningHub = new Button { Name = "NavLearningHub", Content = "学习计划", HorizontalAlignment = HorizontalAlignment.Stretch };
        _navLearningHub.Classes.Add("nav");
        _navLearningHub.Click += (_, _) => ShowPage("learning");
        this.FindControl<StackPanel>("LearningNavHost")!.Children.Add(_navLearningHub);

        // 拼写练习全屏宿主
        _typingHost = new Border { IsVisible = false };
        _typingHost.Bind(Border.BackgroundProperty, this.GetResourceObservable("StudyGradientBrush"));
        Grid.SetRow(_typingHost, 1);
        _typingHost.SetValue(Panel.ZIndexProperty, 100);
        ((Grid)RootWindowBorder.Child!).Children.Add(_typingHost);

        ReloadLearningPlans();
    }

    private void ReorderLearningNavigation()
    {
        var host = this.FindControl<StackPanel>("LearningNavHost");
        if (host == null) return;

        var preferred = new Control?[]
        {
            _navLearningHub,
            _statisticsNav,
            _examTopicsNav,
            _socialNav
        }.OfType<Control>().Distinct().ToList();
        var ordered = preferred
            .Concat(host.Children.OfType<Control>().Where(control => !preferred.Contains(control)))
            .ToList();

        host.Children.Clear();
        foreach (var control in ordered) host.Children.Add(control);
    }

    private static TextBlock LearningText(string text, double size = 14) => new() { Text = UiText.Text(text), FontSize = size, TextWrapping = TextWrapping.Wrap };

    private static TextBlock LearningBilingualText(string chinese, string english, double size = 14) =>
        new() { Text = UiText.Bilingual(chinese, english), FontSize = size, TextWrapping = TextWrapping.Wrap };

    private static Border LearningCard(Control child)
    {
        var card = new Border { Child = child, Padding = new Thickness(18) };
        card.Classes.Add("card");
        return card;
    }

    private Button LearningButton(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = UiText.Text(label), Padding = new Thickness(14, 8) };
        button.Classes.Add(primary ? "primary" : "secondary");
        button.Click += (_, _) =>
        {
            if (_restoring || !_databaseAvailable)
            {
                SetStatus(UiText.Bilingual("词库正在恢复或不可用，请稍后再试。",
                    "The vocabulary archive is restoring or unavailable. Try again shortly."));
                return;
            }
            try { action(); }
            catch (Exception ex)
            {
                SetStatus(UiText.Bilingual("学习操作失败：", "Learning action failed: ") + ex.Message);
            }
        };
        return button;
    }

    private Button LearningBilingualButton(string chinese, string english, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = UiText.Bilingual(chinese, english),
            Padding = new Thickness(14, 8)
        };
        button.Classes.Add(primary ? "primary" : "secondary");
        button.Click += (_, _) =>
        {
            if (_restoring || !_databaseAvailable)
            {
                SetStatus(UiText.Bilingual("词库正在恢复或不可用，请稍后再试。",
                    "The vocabulary archive is restoring or unavailable. Try again shortly."));
                return;
            }
            try { action(); } catch (Exception ex) { SetStatus(UiText.Bilingual("学习操作失败：", "Learning action failed: ") + ex.Message); }
        };
        return button;
    }

    private Button RatingButton(string label, StudyRating rating)
    {
        var button = new Button
        {
            Content = UiText.Text(label),
            MinWidth = 106,
            Padding = new Thickness(8, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        // 统一主题中性，不强行红绿蓝
        button.Classes.Add("secondary");
        button.Click += (_, _) =>
        {
            if (_restoring || !_databaseAvailable)
            {
                SetStatus(UiText.Bilingual("词库正在恢复或不可用，请稍后再试。",
                    "The vocabulary archive is restoring or unavailable. Try again shortly."));
                return;
            }
            try { RateDailyLearning(rating); }
            catch (Exception ex)
            {
                SetStatus(UiText.Bilingual("评分失败：", "Rating failed: ") + ex.Message);
            }
        };
        return button;
    }

    private void ShowLearningHub(bool visible)
    {
        if (_learningHubPage == null) return;
        _learningHubPage.IsVisible = visible;
        if (_typingHost != null) _typingHost.IsVisible = visible && _learningTypingSession != null;
        if (!visible)
        {
            ClearLearningPlanQueue();
            _studyWorkspaceHost.IsVisible = false;
            if (_managementHost != null) _managementHost.IsVisible = true;
        }
        _navLearningHub.Classes.Set("active", visible);
        if (!visible) return;

        var path = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "daily-plans.json");
        if (!string.Equals(path, _learningPlanPath, StringComparison.OrdinalIgnoreCase)) ReloadLearningPlans();

        RenderLearningPlans();
        if (_learningTypingSession == null && _dailyLearningSession != null && _studyWorkspaceHost.IsVisible)
        {
            RenderDailyLearning();
        }
    }

    private void ReloadLearningPlans()
    {
        LoadIeltsProgress();
        ClearLearningPlanQueue();
        _activeLearningPlan = null;
        _dailyLearningSession = null;
        _learningTypingSession = null;
        if (_typingHost != null) _typingHost.IsVisible = false;

        _studyWorkspaceHost.Children.Clear();
        _studyWorkspaceHost.IsVisible = false;
        _managementHost.IsVisible = true;

        _learningPlanPath = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "daily-plans.json");
        _learningPlanStore = new DailyStudyPlanStore(_learningPlanPath);

        try
        {
            _learningPlans = _learningPlanStore.Load();
            _learningPlansLoadFailed = false;
            _learningNotice.Text = UiText.Text("已完成的计划进度会自动保存；切换页面保留本轮连击，重启后完整恢复未完成轮次与连击状态。");
        }
        catch (Exception ex)
        {
            _learningPlans = [];
            _learningPlansLoadFailed = true;
            _learningNotice.Text = "无法读取学习计划，已保留原文件且暂停计划写入。请备份并修复 " + _learningPlanPath + "。原因：" + ex.Message;
            SetStatus("计划文件读取失败，原文件未覆盖。");
        }

        RenderLearningPlans();
    }

    private bool SaveLearningPlans(IReadOnlyList<DailyStudyPlan> plans, bool intentionalPlanChange = false)
    {
        if (_restoring || !_databaseAvailable || _learningPlansLoadFailed || _learningPlanStore == null)
        {
            SetStatus("当前不能保存计划，请先解决词库或计划文件的问题。");
            return false;
        }

        if (!ReplayMemoryJournal()) return false;

        var currentPath = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "daily-plans.json");
        if (!string.Equals(currentPath, _learningPlanPath, StringComparison.OrdinalIgnoreCase))
        {
            ReloadLearningPlans();
            SetStatus("词库位置已改变，请重新选择计划。");
            return false;
        }

        try
        {
            _learningPlanStore.Save(plans);
            // 本地写入成功之后才向“一起学习”观察；社交失败不影响本地学习。
            SocialObserveSaved(SocialFactProjection.SavedCause(_socialPendingFirstLearning, intentionalPlanChange), plans: plans);
            _socialPendingFirstLearning = false;
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("计划保存失败，操作已撤回：" + ex.Message);
            return false;
        }
    }

    private List<DailyStudyPlanWord> ArchivePlanWords()
    {
        var selected = _allWords.Where(w => w.Selected).ToList();
        return (selected.Count > 0 ? selected : _allWords).Select(w => new DailyStudyPlanWord
        {
            Id = w.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Word = w.Word,
            Meaning = w.Translation,
            Phonetic = w.Phonetic,
            Definition = w.Definition,
            Example = string.Join("\n", w.AiExamples.Select(e => e.English + "\n" + e.Chinese))
        }).ToList();
    }

    private void CreateLearningPlan()
    {
        if (!int.TryParse(_planQuota.Text, out var quota) || quota is < 1 or > 10000)
        {
            SetStatus("每日词数请输入 1–10000 的整数。");
            return;
        }

        var words = ArchivePlanWords();
        if (words.Count == 0)
        {
            SetStatus("词汇档案为空，请先收藏或导入词汇。");
            return;
        }

        var plan = DailyStudyPlanRules.Create(_planName.Text ?? "", DailyStudyPlanSource.Archive, "词汇档案", words, quota, _planRandom.IsChecked == true, Random.Shared.Next());
        var overlaps = DailyStudyPlanRules.FindOverlaps(_learningPlans, plan);
        var updated = _learningPlans.Append(plan).ToList();

        if (!SaveLearningPlans(updated, intentionalPlanChange: true)) return;

        _learningPlans = updated;
        RenderLearningPlans();
        SetStatus(overlaps.Count > 0
            ? $"计划已创建；与其他进行中计划有 {overlaps.Select(o => o.Word).Distinct().Count()} 个重复词，进度分别记录。"
            : $"计划已创建，共 {words.Count} 个词。");
    }

    private void ReplaceLearningPlan(DailyStudyPlan previous, DailyStudyPlan replacement)
    {
        var updated = _learningPlans.Select(p => p.Id == previous.Id ? replacement : p).ToList();
        if (!SaveLearningPlans(updated, intentionalPlanChange: true)) return;

        _learningPlans = updated;
        if (_activeLearningPlan?.Id == previous.Id)
        {
            ClearLearningPlanQueue();
            _activeLearningPlan = null;
            _dailyLearningSession = null;
            _studyWorkspaceHost.Children.Clear();
            _studyWorkspaceHost.IsVisible = false;
            _managementHost.IsVisible = true;
        }

        RenderLearningPlans();
        SetStatus("计划已保存，已完成进度保留。");
    }

    private static DailyStudyPlan CloneLearningPlan(DailyStudyPlan plan) =>
        System.Text.Json.JsonSerializer.Deserialize<DailyStudyPlan>(System.Text.Json.JsonSerializer.Serialize(plan))!;

    private void RenderLearningPlans()
    {
        if (!_learningPlansLoadFailed &&
            !string.IsNullOrWhiteSpace(_learningPlanDetailId) &&
            _learningPlans.FirstOrDefault(plan => plan.Id == _learningPlanDetailId) is { } detailedPlan)
        {
            _managementHost.Content = BuildLearningPlanDetails(detailedPlan);
            _managementHost.IsVisible = true;
            return;
        }
        if (_learningPlansLoadFailed ||
            !_learningPlans.Any(plan => plan.Id == _learningPlanDetailId))
            _learningPlanDetailId = null;

        if (_learningManagementOverview != null &&
            !ReferenceEquals(_managementHost.Content, _learningManagementOverview))
            _managementHost.Content = _learningManagementOverview;

        _learningPlansPanel.Children.Clear();
        var today = LearningDay.TodayBjt(DateTime.UtcNow);
        var dueWordKeys = QueryDueWordKeys();
        var filtered = _learningPlans.Where(LearningPlanMatchesFilter).ToList();
        var classified = filtered
            .Select(plan =>
            {
                var batch = DailyStudyPlanRules.GetTodayBatch(plan, today);
                var dueWords = GetPlanDueReviewWords(plan, dueWordKeys);
                return new
                {
                    Plan = plan,
                    Group = DailyStudyPlanRules.ClassifyHomeGroup(
                        plan, today, hasDueReview: dueWords.Count > 0)
                };
            })
            .ToList();
        var todayIncomplete = classified
            .Where(item => item.Group == DailyStudyPlanHomeGroup.TodayIncomplete)
            .Select(item => item.Plan)
            .DistinctBy(plan => plan.Id)
            .ToList();
        var completedToday = classified
            .Where(item => item.Group == DailyStudyPlanHomeGroup.TodayCompleted)
            .Select(item => item.Plan)
            .DistinctBy(plan => plan.Id)
            .ToList();
        var completed = classified
            .Where(item => item.Group == DailyStudyPlanHomeGroup.HistoryCompleted)
            .Select(item => item.Plan)
            .DistinctBy(plan => plan.Id)
            .ToList();
        var stopped = classified
            .Where(item => item.Group == DailyStudyPlanHomeGroup.Stopped)
            .Select(item => item.Plan)
            .DistinctBy(plan => plan.Id)
            .ToList();

        if (_learningPlansLoadFailed)
        {
            var errBorder = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(8) };
            errBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("TintBrush"));
            errBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
            errBorder.BorderThickness = new Thickness(1);
            var errLayout = new StackPanel { Spacing = 8 };
            errLayout.Children.Add(LearningBilingualText("计划文件读取失败，原文件已保留。",
                "The learning plan file could not be read. The original file has been preserved.", 14));
            errLayout.Children.Add(LearningBilingualButton("修复后重新读取计划文件",
                "Reload plan file after fixing it", ReloadLearningPlans));
            errBorder.Child = errLayout;
            _learningPlansPanel.Children.Add(errBorder);
            return;
        }

        // 统一空状态卡：无计划时不重复空分组
        if (_learningPlans.Count == 0)
        {
            var emptyCard = new Border
            {
                Padding = new Thickness(32, 28),
                CornerRadius = new CornerRadius(12)
            };
            emptyCard.Classes.Add("card");

            var emptyLayout = new StackPanel
            {
                Spacing = 14,
                MaxWidth = 580,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var emptyTitle = LearningBilingualText("暂无学习计划", "No learning plans yet", 20);
            emptyTitle.FontWeight = FontWeight.SemiBold;
            emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            emptyLayout.Children.Add(emptyTitle);

            var emptyDescription = LearningBilingualText(
                "从考试专题或词汇档案中选词，安排每天的学习量。\n完成首学后，FSRS 会根据实际记忆表现安排后续复习；到期词进入「今日重逢」。",
                "Choose words from exam topics or your vocabulary archive and set a daily workload.\nAfter first learning, FSRS schedules later review from your actual recall performance.",
                14);
            emptyDescription.Opacity = 0.8;
            emptyDescription.TextAlignment = TextAlignment.Center;
            emptyLayout.Children.Add(emptyDescription);

            var promptText = LearningBilingualText(
                "请点击右上角「创建学习计划 ▾」选择来源并开始创建。",
                "Use Create learning plan in the upper-right corner to choose a source.",
                13);
            promptText.Opacity = 0.65;
            promptText.HorizontalAlignment = HorizontalAlignment.Center;
            promptText.Margin = new Thickness(0, 6, 0, 0);
            emptyLayout.Children.Add(promptText);

            emptyCard.Child = emptyLayout;
            _learningPlansPanel.Children.Add(emptyCard);
            return;
        }

        var todayOverviewScope = filtered
            .Where(plan => plan.Status != DailyStudyPlanStatus.Stopped)
            .DistinctBy(plan => plan.Id)
            .ToList();
        _learningPlansPanel.Children.Add(BuildTodayLearningOverview(
            todayOverviewScope, today, dueWordKeys));

        var todayHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        todayHeading.Children.Add(LearningBilingualText("今日涉及的计划", "Plans for today", 20));
        var todayCount = LearningBilingualText($"{todayIncomplete.Count} 个待完成",
            $"{todayIncomplete.Count} remaining", 12);
        todayCount.Opacity = 0.7;
        todayCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(todayCount, 1);
        todayHeading.Children.Add(todayCount);
        _learningPlansPanel.Children.Add(todayHeading);

        if (todayIncomplete.Count > 0)
            RenderPlanBoardGroup(todayIncomplete, today, dueWordKeys);
        else
        {
            var filterEmpty = new Border
            {
                Padding = new Thickness(18),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1)
            };
            filterEmpty.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBrush"));
            filterEmpty.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
            filterEmpty.Child = LearningBilingualText(
                "今日待完成计划已清空，可以查看已完成计划或继续拼写练习。",
                "Nothing remains for today. Review completed plans or continue spelling practice.",
                14);
            _learningPlansPanel.Children.Add(filterEmpty);
        }

        if (completedToday.Count > 0)
        {
            var completedTodayHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            completedTodayHeading.Children.Add(
                LearningBilingualText("今日已完成", "Completed today", 18));
            var completedTodayCount = LearningBilingualText(
                $"{completedToday.Count} 个计划", $"{completedToday.Count} plans", 12);
            completedTodayCount.Opacity = 0.7;
            completedTodayCount.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(completedTodayCount, 1);
            completedTodayHeading.Children.Add(completedTodayCount);
            _learningPlansPanel.Children.Add(completedTodayHeading);
            RenderPlanBoardGroup(completedToday, today, dueWordKeys);
        }

        if (completed.Count > 0)
        {
            var toggleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var toggleTitle = LearningBilingualText(
                $"已完成计划 ({completed.Count})",
                $"Completed plans ({completed.Count})",
                16);
            toggleRow.Children.Add(toggleTitle);

            var toggleBtn = _completedPlansExpanded
                ? LearningBilingualButton("收起 ▴", "Collapse ▴", () =>
                {
                    _completedPlansExpanded = false;
                    RenderLearningPlans();
                })
                : LearningBilingualButton("展开 ▾", "Expand ▾", () =>
                {
                    _completedPlansExpanded = true;
                    RenderLearningPlans();
                });
            Grid.SetColumn(toggleBtn, 1);
            toggleRow.Children.Add(toggleBtn);

            _learningPlansPanel.Children.Add(toggleRow);

            if (_completedPlansExpanded)
                RenderPlanBoardGroup(completed, today, dueWordKeys);
        }

        if (stopped.Count > 0)
        {
            var stoppedRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            stoppedRow.Children.Add(LearningBilingualText(
                $"已停止的计划 ({stopped.Count})",
                $"Stopped plans ({stopped.Count})",
                16));
            var stoppedButton = _stoppedPlansExpanded
                ? LearningBilingualButton("收起 ▴", "Collapse ▴", () =>
                {
                    _stoppedPlansExpanded = false;
                    RenderLearningPlans();
                })
                : LearningBilingualButton("展开 ▾", "Expand ▾", () =>
                {
                    _stoppedPlansExpanded = true;
                    RenderLearningPlans();
                });
            Grid.SetColumn(stoppedButton, 1);
            stoppedRow.Children.Add(stoppedButton);
            _learningPlansPanel.Children.Add(stoppedRow);
            if (_stoppedPlansExpanded)
                RenderPlanBoardGroup(stopped, today, dueWordKeys);
        }
    }

    private bool LearningPlanMatchesFilter(DailyStudyPlan plan) => _learningPlanFilter switch
    {
        "exam" => IsExamPlan(plan) || plan.Source == DailyStudyPlanSource.Ielts,
        "archive" => plan.Source == DailyStudyPlanSource.Archive,
        _ => true
    };

    private IReadOnlySet<string> QueryDueWordKeys() =>
        Memory().QueryDue(DateTime.UtcNow, int.MaxValue)
            .Select(card => card.WordKey)
            .ToHashSet(StringComparer.Ordinal);

    private IReadOnlyList<DailyStudyPlanWord> GetPlanDueReviewWords(
        DailyStudyPlan plan, IReadOnlySet<string> dueWordKeys)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(dueWordKeys);
        var archiveById = _allWords
            .GroupBy(word => word.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var due = new List<DailyStudyPlanWord>();
        WordItem? FindArchiveWord(string id)
        {
            if (!long.TryParse(id, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var numericId))
                return null;
            return archiveById.GetValueOrDefault(numericId);
        }

        foreach (var word in plan.Words.Where(word => plan.CompletedWordIds.Contains(word.Id)))
        {
            try
            {
                var key = WordKeyResolver.ResolvePlanWord(plan, word, FindArchiveWord);
                if (dueWordKeys.Contains(key.Key)) due.Add(word);
            }
            catch
            {
                // A stale archive identifier must never surface as a word or break the queue.
            }
        }
        return due;
    }

    private IReadOnlyList<DailyStudyPlanWord> SpellingWordsForPlan(
        DailyStudyPlan plan, DateOnly today, IReadOnlySet<string>? dueWordKeys = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        dueWordKeys ??= QueryDueWordKeys();
        var wordsById = plan.Words
            .GroupBy(word => word.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var candidates = new List<DailyStudyPlanWord>();
        candidates.AddRange(DailyStudyPlanRules.GetTodayBatch(plan, today));
        candidates.AddRange(GetPlanDueReviewWords(plan, dueWordKeys));
        candidates.AddRange(plan.CurrentBatchWordIds
            .Where(plan.CompletedWordIds.Contains)
            .Select(id => wordsById.GetValueOrDefault(id))
            .Where(word => word is not null)
            .Select(word => word!));
        candidates.AddRange(plan.Words.Where(word => plan.CompletedWordIds.Contains(word.Id)));
        return candidates.DistinctBy(word => word.Id, StringComparer.Ordinal).ToList();
    }

    private Border BuildTodayLearningOverview(IReadOnlyList<DailyStudyPlan> plans, DateOnly today,
        IReadOnlySet<string> dueWordKeys)
    {
        var reviewScope = plans.Where(plan => plan.Status != DailyStudyPlanStatus.Stopped)
            .DistinctBy(plan => plan.Id).ToList();
        var dueByPlan = reviewScope.ToDictionary(plan => plan.Id,
            plan => GetPlanDueReviewWords(plan, dueWordKeys), StringComparer.Ordinal);
        var firstBatchByPlan = reviewScope
            .Where(plan => plan.Status == DailyStudyPlanStatus.Active)
            .ToDictionary(plan => plan.Id,
                plan => DailyStudyPlanRules.GetTodayBatch(plan, today), StringComparer.Ordinal);
        var firstRemainingByPlan = reviewScope
            .Where(plan => plan.Status == DailyStudyPlanStatus.Active)
            .ToDictionary(plan => plan.Id,
                plan => DailyStudyPlanRules.GetTodayWords(plan, today), StringComparer.Ordinal);
        var overview = DailyStudyOverviewRules.Build(reviewScope.Select(plan =>
            new DailyStudyOverviewScope(
                plan.Id,
                ResolvePlanWordKeys(new Dictionary<string, IReadOnlyList<DailyStudyPlanWord>>
                {
                    [plan.Id] = firstBatchByPlan.GetValueOrDefault(plan.Id, [])
                }),
                ResolvePlanWordKeys(new Dictionary<string, IReadOnlyList<DailyStudyPlanWord>>
                {
                    [plan.Id] = firstRemainingByPlan.GetValueOrDefault(plan.Id, [])
                }),
                ResolvePlanWordKeys(new Dictionary<string, IReadOnlyList<DailyStudyPlanWord>>
                {
                    [plan.Id] = dueByPlan.GetValueOrDefault(plan.Id, [])
                }))));

        var layout = new StackPanel { Spacing = 18 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var headingStack = new StackPanel { Spacing = 5 };
        var headingText = LearningBilingualText("今日待学习", "Due today", 23);
        headingText.FontWeight = FontWeight.SemiBold;
        headingStack.Children.Add(headingText);
        var scopeText = LearningBilingualText(
            overview.DueReviewWords > 0
                ? "先按要求完成到期复习，再继续今日首次学习。"
                : "按自己的节奏完成今日词卡与拼写练习。",
            overview.DueReviewWords > 0
                ? "Complete due reviews first, then continue today's first learning."
                : "Work through today's cards and spelling practice at your own pace.",
            12);
        scopeText.Opacity = 0.7;
        headingStack.Children.Add(scopeText);
        heading.Children.Add(headingStack);
        var planCount = LearningBilingualText(
            $"{today:MM月dd日} · {overview.TodayPlanCount} 个计划",
            $"{today.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)} · {overview.TodayPlanCount} {(overview.TodayPlanCount == 1 ? "plan" : "plans")}",
            12);
        planCount.Opacity = 0.68;
        planCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(planCount, 1);
        heading.Children.Add(planCount);
        layout.Children.Add(heading);

        Control OverviewMetric(string value, string chineseLabel, string englishLabel, bool accent)
        {
            var metric = new StackPanel { Spacing = 5 };
            var number = new TextBlock
            {
                Text = value,
                FontSize = accent ? 38 : 26,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            if (accent) number.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AccentBrush"));
            metric.Children.Add(number);
            var label = LearningBilingualText(chineseLabel, englishLabel, 12);
            label.Opacity = 0.68;
            metric.Children.Add(label);
            return metric;
        }

        var metrics = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.25*,*,*"),
            ColumnSpacing = 24
        };
        metrics.Children.Add(OverviewMetric(
            $"{overview.FirstLearnCompletedWords} / {overview.FirstLearnBatchWords}",
            "已完成 / 今日总词数", "Completed / today's words", accent: true));
        var dueMetric = OverviewMetric(
            overview.DueReviewWords.ToString(), "今日到期复习", "Due reviews", accent: false);
        Grid.SetColumn(dueMetric, 1);
        metrics.Children.Add(dueMetric);
        var remainingMetric = OverviewMetric(
            overview.AttentionRemainingWords.ToString(), "剩余待处理", "Still remaining", accent: false);
        Grid.SetColumn(remainingMetric, 2);
        metrics.Children.Add(remainingMetric);
        layout.Children.Add(metrics);

        var progressHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        progressHeader.Children.Add(LearningBilingualText(
            overview.FirstLearnBatchWords > 0
                ? $"今日首学进度 · {overview.FirstLearnCompletedWords}/{overview.FirstLearnBatchWords}"
                : "今日首学进度",
            overview.FirstLearnBatchWords > 0
                ? $"First-learning progress · {overview.FirstLearnCompletedWords}/{overview.FirstLearnBatchWords}"
                : "First-learning progress", 12));
        var percent = LearningBilingualText(
            $"{overview.FirstLearnCompletionRatio:P0}",
            $"{overview.FirstLearnCompletionRatio:P0}", 11);
        percent.Opacity = 0.66;
        percent.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(percent, 1);
        progressHeader.Children.Add(percent);
        layout.Children.Add(progressHeader);

        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = Math.Clamp(overview.FirstLearnCompletionRatio, 0, 1),
            Height = 7
        };
        progress.Classes.Add("plan-progress");
        layout.Children.Add(progress);

        layout.Children.Add(LearningBilingualText(
            overview.DueReviewWords > 0
                ? "复习按 FSRS 到期时间排序；曲线表示预测，不是排名或考试分数。"
                : overview.FirstLearnComplete
                    ? "今日首学已完成；计划仍会按 FSRS 到期时间继续复习。"
                    : "当前没有到期复习，可以继续首次学习或进入指定计划的拼写练习。",
            overview.DueReviewWords > 0
                ? "Reviews are ordered by FSRS due time. Curves are predictions, not rankings or exam scores."
                : overview.FirstLearnComplete
                    ? "First learning is complete for today. FSRS-due reviews remain available."
                    : "No reviews are due. Continue first learning or open spelling practice for a plan.",
            12));

        layout.Children.Add(BuildLearningPlanFilters());

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var reviewButton = LearningBilingualButton("开始今日复习", "Start today's review", () =>
            StartLearningPlanQueue(reviewScope, StudyMode.Review, today, dueWordKeys),
            primary: overview.DueReviewWords > 0);
        reviewButton.IsEnabled = overview.DuePlanCount > 0;
        reviewButton.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(reviewButton);

        var firstLearnButton = LearningBilingualButton("继续首次学习", "Continue first learning", () =>
            StartLearningPlanQueue(reviewScope, StudyMode.FirstLearn, today, dueWordKeys),
            primary: overview.DueReviewWords == 0 && overview.FirstLearnRemainingWords > 0);
        firstLearnButton.IsEnabled = overview.FirstLearnRemainingWords > 0;
        firstLearnButton.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(firstLearnButton);
        layout.Children.Add(actions);

        var card = LearningCard(layout);
        card.Padding = new Thickness(28, 24);
        return card;
    }

    private Control BuildLearningPlanFilters()
    {
        var panel = new StackPanel { Spacing = 8 };
        var label = LearningBilingualText("查看范围", "View", 11);
        label.Opacity = 0.62;
        panel.Children.Add(label);

        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(BuildLearningPlanFilterButton("all", "全部", "All"));
        filters.Children.Add(BuildLearningPlanFilterButton("exam", "考试专题", "Exam topics"));
        filters.Children.Add(BuildLearningPlanFilterButton("archive", "词汇档案", "Vocabulary"));
        panel.Children.Add(filters);
        return panel;
    }

    private HashSet<string> ResolvePlanWordKeys(
        IReadOnlyDictionary<string, IReadOnlyList<DailyStudyPlanWord>> wordsByPlan)
    {
        var archiveById = _allWords
            .GroupBy(word => word.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in _learningPlans)
        {
            if (!wordsByPlan.TryGetValue(plan.Id, out var words)) continue;
            foreach (var word in words)
            {
                try
                {
                    var key = WordKeyResolver.ResolvePlanWord(plan, word, id =>
                        long.TryParse(id, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var numericId)
                            ? archiveById.GetValueOrDefault(numericId)
                            : null);
                    keys.Add(key.Key);
                }
                catch
                {
                    // Stale legacy identifiers do not contribute to the daily overview.
                }
            }
        }
        return keys;
    }

    private Button BuildLearningPlanFilterButton(string key, string chinese, string english)
    {
        var selected = _learningPlanFilter == key;
        var button = LearningBilingualButton(chinese, english, () =>
        {
            _learningPlanFilter = key;
            RenderLearningPlans();
        }, selected);
        button.Margin = new Thickness(0, 0, 8, 4);
        button.Padding = new Thickness(14, 6);
        return button;
    }

    private void OpenLearningPlanDetails(DailyStudyPlan plan)
    {
        ClearLearningPlanQueue();
        _learningPlanDetailId = plan.Id;
        _managementHost.IsVisible = true;
        _studyWorkspaceHost.IsVisible = false;
        if (_typingHost != null) _typingHost.IsVisible = false;
        RenderLearningPlans();
    }

    private void CloseLearningPlanDetails()
    {
        _learningPlanDetailId = null;
        RenderLearningPlans();
    }

    private Control BuildLearningPlanDetails(DailyStudyPlan plan)
    {
        var today = LearningDay.TodayBjt(DateTime.UtcNow);
        var batch = DailyStudyPlanRules.GetTodayBatch(plan, today);
        var completedToday = DailyStudyPlanRules.TodayCompleted(plan, today);
        var dueWordKeys = QueryDueWordKeys();
        var reviewWords = GetPlanDueReviewWords(plan, dueWordKeys);
        var spellingWords = SpellingWordsForPlan(plan, today, dueWordKeys);
        var firstLearnRemaining = batch.Count(word => !plan.CompletedWordIds.Contains(word.Id));

        var page = new StackPanel
        {
            Spacing = 18,
            Margin = new Thickness(28, 22),
            MaxWidth = 1040,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var backButton = new Button
        {
            Name = "LearningPlanDetailsBack",
            Content = UiText.Bilingual("← 学习计划", "← Learning plans"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 42,
            Padding = new Thickness(16, 10),
            FontSize = 13
        };
        backButton.Classes.Add("secondary");
        backButton.Bind(Button.BackgroundProperty, this.GetResourceObservable("TintBrush"));
        backButton.Bind(Button.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        backButton.Click += (_, _) => CloseLearningPlanDetails();
        page.Children.Add(backButton);

        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titleStack = new StackPanel { Spacing = 6 };
        var title = new TextBlock
        {
            Text = plan.Name,
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        titleStack.Children.Add(title);
        var source = new TextBlock
        {
            Text = UiText.Bilingual("来源：", "Source: ") + PlanSourceDisplay(plan),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };
        source.Opacity = 0.72;
        titleStack.Children.Add(source);
        titleRow.Children.Add(titleStack);

        var moreButton = new Button
        {
            Content = "•••",
            Padding = new Thickness(10, 6),
            VerticalAlignment = VerticalAlignment.Top
        };
        moreButton.Classes.Add("secondary");
        ToolTip.SetTip(moreButton, UiText.Bilingual("计划管理", "Plan management"));
        moreButton.Click += (_, _) => ShowPlanManagementMenu(plan, moreButton);
        Grid.SetColumn(moreButton, 1);
        titleRow.Children.Add(moreButton);
        page.Children.Add(titleRow);

        var metricsContent = new StackPanel { Spacing = 16 };
        var metricsRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var todayMetric = BuildLearningPlanDetailMetric(
            $"{completedToday}/{batch.Count}", "今日首学", "First learn today");
        metricsRow.Children.Add(todayMetric);
        var overallMetric = BuildLearningPlanDetailMetric(
            $"{plan.CompletedWordIds.Count}/{plan.Words.Count}", "总体进度", "Overall progress");
        Grid.SetColumn(overallMetric, 1);
        metricsRow.Children.Add(overallMetric);
        var daysMetric = BuildLearningPlanDetailMetric(
            DailyStudyPlanRules.EstimatedDaysRemaining(plan).ToString(), "预计天数", "Days left");
        Grid.SetColumn(daysMetric, 2);
        metricsRow.Children.Add(daysMetric);
        metricsContent.Children.Add(metricsRow);

        var overallProgress = new StackPanel { Spacing = 6 };
        overallProgress.Children.Add(LearningBilingualText(
            $"总体进度 · 已完成 {plan.CompletedWordIds.Count}/{plan.Words.Count} 词",
            $"Overall progress · {plan.CompletedWordIds.Count}/{plan.Words.Count} words completed",
            12));
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, plan.Words.Count),
            Value = Math.Min(plan.Words.Count, plan.CompletedWordIds.Count),
            Height = 8
        };
        progress.Classes.Add("plan-progress");
        overallProgress.Children.Add(progress);
        metricsContent.Children.Add(overallProgress);
        var summaryCard = LearningCard(metricsContent);
        summaryCard.Padding = new Thickness(22, 20);
        page.Children.Add(summaryCard);

        var entries = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions("Auto")
        };
        var canFirstLearn = plan.Status == DailyStudyPlanStatus.Active && firstLearnRemaining > 0;
        var firstEntry = BuildLearningPlanEntryCard(
            "01", "首次学习", "First learning",
            canFirstLearn
                ? UiText.Bilingual($"本轮剩余 {firstLearnRemaining} 词", $"{firstLearnRemaining} words left in this round")
                : plan.Status == DailyStudyPlanStatus.Active
                    ? UiText.Bilingual("今日首次学习已完成", "First learning is complete for today")
                    : UiText.Bilingual("计划当前不可学习", "This plan is not available for study"),
            canFirstLearn ? () => StartDailyLearning(plan, StudyMode.FirstLearn) : null,
            primary: true);
        entries.Children.Add(firstEntry);

        var canReview = plan.Status != DailyStudyPlanStatus.Stopped && reviewWords.Count > 0;
        var reviewDescription = plan.Status == DailyStudyPlanStatus.Stopped
            ? UiText.Bilingual("计划已停止，无法复习", "This plan is stopped and cannot be reviewed")
            : reviewWords.Count > 0
                ? UiText.Bilingual($"{reviewWords.Count} 个 FSRS 到期词", $"{reviewWords.Count} FSRS words due now")
                : UiText.Bilingual("当前没有到期词", "No words are due right now");
        var reviewEntry = BuildLearningPlanEntryCard(
            "02", "今日复习", "Today's review", reviewDescription,
            canReview ? () => StartDailyLearning(plan, StudyMode.Review) : null);
        Grid.SetColumn(reviewEntry, 1);
        entries.Children.Add(reviewEntry);

        var canSpell = plan.Status != DailyStudyPlanStatus.Stopped && spellingWords.Count > 0;
        var spellingEntry = BuildLearningPlanEntryCard(
            "03", "拼写练习", "Spelling practice",
            canSpell
                ? UiText.Bilingual($"{spellingWords.Count} 个计划词 · 可选择淡写或默写",
                    $"{spellingWords.Count} plan words · choose guided or dictation mode")
                : UiText.Bilingual("暂无可练习词", "No words available for practice"),
            canSpell
                ? () => StartLearningTyping(spellingWords.Select(ToTypingWord).ToList(), true,
                    returnPlanId: plan.Id)
                : null,
            alternateAction: canSpell
                ? () => StartLearningTyping(spellingWords.Select(ToTypingWord).ToList(), false,
                    returnPlanId: plan.Id)
                : null,
            alternateChinese: "默写",
            alternateEnglish: "Dictation");
        Grid.SetColumn(spellingEntry, 2);
        entries.Children.Add(spellingEntry);
        entries.SizeChanged += (_, _) =>
        {
            var narrow = entries.Bounds.Width > 0 && entries.Bounds.Width < 660;
            entries.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*,*");
            entries.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto" : "Auto");
            for (var index = 0; index < entries.Children.Count; index++)
            {
                var child = entries.Children[index];
                Grid.SetColumn(child, narrow ? 0 : index);
                Grid.SetRow(child, narrow ? index : 0);
                child.Margin = new Thickness(0, 0, !narrow && index < 2 ? 12 : 0,
                    narrow && index < 2 ? 12 : 0);
            }
        };
        page.Children.Add(entries);

        var detailInfo = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 18
        };
        detailInfo.Children.Add(BuildLearningPlanInfo(
            "每日目标", "Daily target", $"{plan.DailyWordCount} 词 / 天", $"{plan.DailyWordCount} words / day"));
        var batchInfo = BuildLearningPlanInfo(
            "当前批次", "Current batch",
            $"{plan.CurrentBatchWordIds.Count} 词",
            $"{plan.CurrentBatchWordIds.Count} words");
        Grid.SetColumn(batchInfo, 1);
        detailInfo.Children.Add(batchInfo);
        var forgotInfo = BuildLearningPlanInfo(
            "本批困难词", "Difficult in batch",
            $"{plan.ForgotWordIds.Count} 词",
            $"{plan.ForgotWordIds.Count} words");
        Grid.SetColumn(forgotInfo, 2);
        detailInfo.Children.Add(forgotInfo);
        var lastDate = plan.LastBatchCompletedDate is { } completedDate
            ? completedDate.ToString("MM/dd", System.Globalization.CultureInfo.CurrentCulture)
            : UiText.Bilingual("暂无", "Not yet");
        var lastInfo = BuildLearningPlanInfo("最近完成", "Last completed", lastDate, lastDate);
        Grid.SetColumn(lastInfo, 3);
        detailInfo.Children.Add(lastInfo);
        detailInfo.SizeChanged += (_, _) =>
        {
            var narrow = detailInfo.Bounds.Width > 0 && detailInfo.Bounds.Width < 650;
            detailInfo.ColumnDefinitions = new ColumnDefinitions(narrow ? "*,*" : "*,*,*,*");
            detailInfo.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            for (var index = 0; index < detailInfo.Children.Count; index++)
            {
                var child = detailInfo.Children[index];
                Grid.SetColumn(child, narrow ? index % 2 : index);
                Grid.SetRow(child, narrow ? index / 2 : 0);
                child.Margin = new Thickness(0, 0,
                    !narrow && index < detailInfo.Children.Count - 1 ? 0 : 0,
                    narrow && index < 2 ? 12 : 0);
            }
        };
        page.Children.Add(LearningCard(detailInfo));

        var wordPreviewBody = new StackPanel { Spacing = 12 };
        var wordPreviewHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        wordPreviewHeader.Children.Add(LearningBilingualText("计划词汇预览", "Plan vocabulary", 18));
        var previewCount = LearningBilingualText(
            $"共 {plan.Words.Count} 词", $"{plan.Words.Count} words total", 12);
        previewCount.Opacity = 0.65;
        previewCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(previewCount, 1);
        wordPreviewHeader.Children.Add(previewCount);
        wordPreviewBody.Children.Add(wordPreviewHeader);
        wordPreviewBody.Children.Add(BuildLearningPlanWordPreview(plan));
        wordPreviewBody.Children.Add(LearningBilingualText(
            "每个词书与章节使用独立身份；同词出现在不同词书中不会串档。",
            "Every book and section keeps a separate identity, so the same spelling never merges progress across books.",
            12));
        page.Children.Add(LearningCard(wordPreviewBody));

        var planStatus = plan.Status switch
        {
            DailyStudyPlanStatus.Active => UiText.Bilingual("进行中", "In progress"),
            DailyStudyPlanStatus.Stopped => UiText.Bilingual("已停止", "Stopped"),
            _ => UiText.Bilingual("已完成", "Completed")
        };
        page.Children.Add(new TextBlock
        {
            Text = UiText.Bilingual("计划状态：", "Plan status: ") + planStatus,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        return page;
    }

    private Control BuildLearningPlanInfo(string chineseLabel, string englishLabel,
        string chineseValue, string englishValue)
    {
        var panel = new StackPanel { Spacing = 4 };
        var label = LearningBilingualText(chineseLabel, englishLabel, 11);
        label.Opacity = 0.64;
        panel.Children.Add(label);
        var value = LearningBilingualText(chineseValue, englishValue, 16);
        value.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(value);
        return panel;
    }

    private Control BuildLearningPlanWordPreview(DailyStudyPlan plan)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };
        const int previewLimit = 18;
        foreach (var word in plan.Words.Take(previewLimit))
        {
            var chip = new Border
            {
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = word.Word,
                    FontSize = 12.5,
                    FontWeight = FontWeight.Medium
                }
            };
            chip.Bind(Border.BackgroundProperty, this.GetResourceObservable("TintBrush"));
            chip.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
            panel.Children.Add(chip);
        }
        if (plan.Words.Count > previewLimit)
        {
            var more = new Border
            {
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Child = LearningBilingualText(
                    $"+ {plan.Words.Count - previewLimit} 词",
                    $"+ {plan.Words.Count - previewLimit} more",
                    12)
            };
            more.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBrush"));
            more.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
            panel.Children.Add(more);
        }
        return panel;
    }

    private static StackPanel BuildLearningPlanDetailMetric(string value, string chineseLabel, string englishLabel)
    {
        var metric = new StackPanel { Spacing = 4 };
        metric.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 28,
            FontWeight = FontWeight.SemiBold
        });
        var label = LearningBilingualText(chineseLabel, englishLabel, 12);
        label.Opacity = 0.72;
        metric.Children.Add(label);
        return metric;
    }

    private Control BuildLearningPlanEntryCard(string number, string chineseTitle, string englishTitle,
        string description, Action? action, bool primary = false, Action? alternateAction = null,
        string alternateChinese = "", string alternateEnglish = "")
    {
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var numberText = new TextBlock
        {
            Text = number,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.62
        };
        content.Children.Add(numberText);
        var title = LearningBilingualText(chineseTitle, englishTitle, 18);
        title.FontWeight = FontWeight.SemiBold;
        Grid.SetRow(title, 1);
        content.Children.Add(title);
        var descriptionText = new TextBlock
        {
            Text = description,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12)
        };
        descriptionText.Opacity = 0.76;
        Grid.SetRow(descriptionText, 2);
        content.Children.Add(descriptionText);

        var buttonText = englishTitle switch
        {
            "First learning" => ("开始首次学习", "Start first learning"),
            "Today's review" => ("开始复习", "Start review"),
            "Spelling practice" => ("淡写", "Guided spelling"),
            _ => ("开始", "Start")
        };
        var actionButton = action is null
            ? LearningBilingualButton("暂不可用", "Unavailable", () => { }, primary)
            : LearningBilingualButton(buttonText.Item1, buttonText.Item2, action, primary);
        actionButton.IsEnabled = action != null;
        actionButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        actionButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        Grid.SetRow(actionButton, 4);
        content.Children.Add(actionButton);
        if (alternateAction != null)
        {
            var alternateButton = LearningBilingualButton(
                alternateChinese, alternateEnglish, alternateAction);
            alternateButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            alternateButton.HorizontalContentAlignment = HorizontalAlignment.Center;
            alternateButton.Margin = new Thickness(0, 7, 0, 0);
            Grid.SetRow(alternateButton, 5);
            content.Children.Add(alternateButton);
        }

        var card = new Border
        {
            MinHeight = 184,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(10),
            Child = content
        };
        card.Classes.Add("card");
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("TintBrush"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        if (action is null && alternateAction is null) card.Opacity = 0.6;
        return card;
    }

    private void SetLearningPlanStopped(DailyStudyPlan plan)
    {
        var copy = CloneLearningPlan(plan);
        DailyStudyPlanRules.Stop(copy);
        ReplaceLearningPlan(plan, copy);
    }

    private void ResumeLearningPlan(DailyStudyPlan plan)
    {
        var copy = CloneLearningPlan(plan);
        DailyStudyPlanRules.Resume(copy);
        ReplaceLearningPlan(plan, copy);
    }

    private void AdjustLearningPlan(DailyStudyPlan plan, string name, int quota, bool random) =>
        ReplaceLearningPlan(plan, DailyStudyPlanRules.Adjust(plan, name, quota, random, Random.Shared.Next()));

    private void ShowLastPlanBatch(DailyStudyPlan plan)
    {
        ClearLearningPlanQueue();
        _learningTypingSession = null;
        var words = plan.CurrentBatchWordIds.Select(id => plan.Words.Single(w => w.Id == id)).ToList();
        var forgotWords = words.Where(w => plan.ForgotWordIds.Contains(w.Id)).ToList();

        _learningPlanDetailId = plan.Id;
        ShowPage("learning");
        _managementHost.IsVisible=false;
        _studyWorkspaceHost.IsVisible=true;
        _studyWorkspaceHost.Children.Clear();
        var content=new StackPanel {Spacing=20,Margin=new Thickness(24),MaxWidth=680,HorizontalAlignment=HorizontalAlignment.Center};
        content.Children.Add(LearningText(plan.Name,24));
        content.Children.Add(LearningBilingualText(
            "选择最近一批词汇进行拼写。拼写练习不会推进首次学习完成。",
            "Practice the latest word batch. Spelling practice does not advance first-learning completion.",
            13));
        content.Children.Add(new PracticeSetupControl(words.Count,forgotWords.Count,
            (onlyWeak,_)=>StartLearningTyping((onlyWeak?forgotWords:words).Select(ToTypingWord).ToList(),true,
                returnPlanId: plan.Id),
            ()=>
            {
                _studyWorkspaceHost.IsVisible=false;
                _managementHost.IsVisible=true;
                OpenLearningPlanDetails(plan);
            }));
        _studyWorkspaceHost.Children.Add(new ScrollViewer {Content=content,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
    }

    private void StartLearningPlanQueue(IReadOnlyList<DailyStudyPlan> plans, StudyMode mode,
        DateOnly today, IReadOnlySet<string> dueWordKeys)
    {
        if (_restoring || !_databaseAvailable)
        {
            SetStatus(UiText.Bilingual("词库正在恢复或不可用，请稍后再试。",
                "The vocabulary archive is restoring or unavailable. Try again shortly."));
            return;
        }

        var queue = plans
            .DistinctBy(plan => plan.Id)
            .Select(plan => new LearningPlanQueueEntry(plan.Id, mode, today))
            .Where(entry => QueueEntryHasWork(entry, dueWordKeys))
            .ToList();
        if (queue.Count == 0)
        {
            SetStatus(mode == StudyMode.FirstLearn
                ? UiText.Bilingual("今日没有待首次学习的计划。",
                    "No plans have first-learning tasks left today.")
                : UiText.Bilingual("当前没有到期复习词。",
                    "No plan review words are due right now."));
            return;
        }

        _learningPlanQueue.Clear();
        _learningPlanQueue.AddRange(queue);
        _learningPlanQueueIndex = 0;
        if (!StartCurrentQueuedLearningPlan())
        {
            ClearLearningPlanQueue();
            RenderLearningPlans();
        }
    }

    private bool StartCurrentQueuedLearningPlan()
    {
        while (_learningPlanQueueIndex >= 0 &&
               _learningPlanQueueIndex < _learningPlanQueue.Count)
        {
            var entry = _learningPlanQueue[_learningPlanQueueIndex];
            var plan = _learningPlans.FirstOrDefault(candidate => candidate.Id == entry.PlanId);
            var dueWordKeys = entry.LearningDate == LearningDay.TodayBjt(DateTime.UtcNow)
                ? QueryDueWordKeys()
                : null;
            if (plan == null || dueWordKeys == null || !QueueEntryHasWork(entry, dueWordKeys))
            {
                _learningPlanQueueIndex++;
                continue;
            }

            if (StartDailyLearning(plan, entry.Mode, preserveQueue: true)) return true;
            return false;
        }
        return false;
    }

    private bool QueueEntryHasWork(LearningPlanQueueEntry entry, IReadOnlySet<string> dueWordKeys)
    {
        var plan = _learningPlans.FirstOrDefault(candidate => candidate.Id == entry.PlanId);
        if (plan == null || entry.LearningDate != LearningDay.TodayBjt(DateTime.UtcNow)) return false;
        return entry.Mode switch
        {
            StudyMode.FirstLearn => plan.Status == DailyStudyPlanStatus.Active &&
                DailyStudyPlanRules.GetTodayWords(plan, entry.LearningDate).Count > 0,
            StudyMode.Review => plan.Status != DailyStudyPlanStatus.Stopped &&
                GetPlanDueReviewWords(plan, dueWordKeys).Count > 0,
            _ => false
        };
    }

    private bool HasNextQueuedLearningPlan()
    {
        if (_learningPlanQueueIndex < 0) return false;
        var dueWordKeys = QueryDueWordKeys();
        for (var index = _learningPlanQueueIndex + 1; index < _learningPlanQueue.Count; index++)
            if (QueueEntryHasWork(_learningPlanQueue[index], dueWordKeys)) return true;
        return false;
    }

    private void AdvanceLearningPlanQueue()
    {
        if (_learningPlanQueue.Count == 0)
        {
            ExitDailyLearning();
            return;
        }

        _learningPlanQueueIndex++;
        if (StartCurrentQueuedLearningPlan()) return;

        ExitDailyLearning();
        SetStatus(UiText.Bilingual("今日计划队列已全部完成。",
            "All plans in today's queue are complete."));
    }

    private void ClearLearningPlanQueue()
    {
        _learningPlanQueue.Clear();
        _learningPlanQueueIndex = -1;
    }

    private bool StartDailyLearning(DailyStudyPlan plan, StudyMode mode, bool preserveQueue = false)
    {
        if (!preserveQueue) ClearLearningPlanQueue();
        if (plan.Status == DailyStudyPlanStatus.Stopped)
        {
            SetStatus(UiText.Bilingual("该计划已停止，恢复后才能继续学习。",
                "This plan is stopped. Resume it before studying."));
            return false;
        }
        if (mode == StudyMode.FirstLearn && plan.Status != DailyStudyPlanStatus.Active)
        {
            SetStatus(UiText.Bilingual("已完成计划只开放到期复习与拼写练习。",
                "A completed plan only allows due review and spelling practice."));
            return false;
        }

        _learningTypingSession = null;
        if (_typingHost != null) _typingHost.IsVisible = false;
        var today = LearningDay.TodayBjt(DateTime.UtcNow);

        if (_activeLearningPlan?.Id != plan.Id ||
            _dailyLearningSession == null ||
            _dailyLearningDate != today ||
            _dailyLearningSession.Mode != mode)
        {
            var dueWordKeys = QueryDueWordKeys();
            var available = mode == StudyMode.FirstLearn
                ? DailyStudyPlanRules.GetTodayWords(plan, today)
                : GetPlanDueReviewWords(plan, dueWordKeys);
            if (available.Count == 0)
            {
                SetStatus(mode == StudyMode.FirstLearn
                    ? UiText.Bilingual("今日首次学习已完成，请明天继续。",
                        "First learning is complete for today. Continue tomorrow.")
                    : UiText.Bilingual("当前没有 FSRS 到期词，请稍后再复习。",
                        "No words are due for FSRS review right now."));
                return false;
            }

            var copy = CloneLearningPlan(plan);
            var session = new DailyStudyPlanSession(copy, today, mode,
                reviewWordIds: mode == StudyMode.Review
                    ? available.Select(word => word.Id).ToList()
                    : null);
            var updated = _learningPlans.Select(p => p.Id == copy.Id ? copy : p).ToList();
            if (!SaveLearningPlans(updated)) return false;

            _learningPlans = updated;
            _activeLearningPlan = copy;
            _dailyLearningSession = session;
            _dailyLearningDate = today;

            // 保持主控 MemoryBridge 钩子调用协议
            _planAnswerVisible = false;
            _planRatedPending = false;
            _planRatedWordId = null;
            _planRatedWordCompleted = false;
            BeginPlanMemory(copy, session);
        }

        // 切换到专注学习工作区
        if (_currentPage != "learning") ShowPage("learning");
        _managementHost.IsVisible = false;
        _studyWorkspaceHost.IsVisible = true;

        RenderDailyLearning();
        RenderLearningPlans();
        SetGlobalFocusChrome(true);
        return true;
    }

    private void StartDailyLearning(DailyStudyPlan plan) =>
        StartDailyLearning(plan, StudyMode.FirstLearn);

    private void ExitDailyLearning()
    {
        ClearLearningPlanQueue();
        PersistLearningSurface("plan");
        _studyWorkspaceHost.IsVisible = false;
        _managementHost.IsVisible = true;
        _planAnswerVisible = false;
        _planRatedPending = false;
        _planRatedWordId = null;
        _planRatedWordCompleted = false;
        RenderLearningPlans();
        SetGlobalFocusChrome(false);
        SetStatus(UiText.Bilingual("学习会话已暂停，进度已实时保存。",
            "The study session is paused and progress has been saved."));
    }

    private void PlayCurrentLearningWordAudio()
    {
        if (_dailyLearningSession?.Round.HasCurrent == true && _activeLearningPlan != null)
        {
            var wordId = _dailyLearningSession.Round.Current;
            var word = _activeLearningPlan.Words.FirstOrDefault(w => w.Id == wordId);
            if (word != null)
            {
                GetLearningAudio().Play(word.Word, PlanWordAudioFile(word));
            }
        }
    }

    private Control BuildStreakDots(int currentStreak, int targetStreak)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (var i = 0; i < targetStreak; i++)
        {
            var isFilled = i < currentStreak;
            var dot = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1.5)
            };
            if (isFilled)
            {
                dot.Bind(Border.BackgroundProperty, this.GetResourceObservable("PrimaryGreen"));
                dot.Bind(Border.BorderBrushProperty, this.GetResourceObservable("PrimaryGreen"));
            }
            else
            {
                dot.Background = Brushes.Transparent;
                dot.Bind(Border.BorderBrushProperty, this.GetResourceObservable("MutedBrush"));
            }
            panel.Children.Add(dot);
        }

        return panel;
    }

    private void RenderDailyLearning()
    {
        _studyWorkspaceHost.Children.Clear();
        if (_dailyLearningSession == null || _activeLearningPlan == null)
        {
            _studyWorkspaceHost.IsVisible = false;
            _managementHost.IsVisible = true;
            return;
        }

        var session = _dailyLearningSession;
        var round = session.Round;
        var sourceLabel = PlanSourceDisplay(_activeLearningPlan);
        var displayWordId = _planRatedPending ? _planRatedWordId : round.Current;
        var showCompletion = round.IsFinished && !_planRatedPending;

        // ==================== 1. 固定页头 (Grid.Row 0) ====================
        var headerBorder = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        headerBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("PaperBrush"));
        headerBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var btnBack = LearningBilingualButton("← 返回计划详情", "← Back to plan details", ExitDailyLearning);
        headerGrid.Children.Add(btnBack);

        var titleStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleText = new TextBlock
        {
            Text = $"{_activeLearningPlan.Name} · {sourceLabel}",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 360
        };
        titleText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        titleStack.Children.Add(titleText);
        if (_learningPlanQueue.Count > 0)
        {
            var queueText = new TextBlock
            {
                Text = UiText.Bilingual(
                    $"计划队列 {_learningPlanQueueIndex + 1} / {_learningPlanQueue.Count}",
                    $"Plan queue {_learningPlanQueueIndex + 1} / {_learningPlanQueue.Count}"),
                FontSize = 11,
                Opacity = 0.68,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            queueText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
            titleStack.Children.Add(queueText);
        }
        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);

        var progressBadge = new TextBlock
        {
            Text = UiText.Bilingual(
                $"已完成 {round.Completed} / {round.Total} · 剩余 {round.Remaining} 词",
                $"{round.Completed} / {round.Total} completed · {round.Remaining} words remaining"),
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };
        progressBadge.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        Grid.SetColumn(progressBadge, 2);
        headerGrid.Children.Add(progressBadge);

        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        _studyWorkspaceHost.Children.Add(headerBorder);

        // ==================== 2. 可滚动中间内容区 (Grid.Row 1 - 仅此区滚动) ====================
        var contentScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
        };

        var cardCenterContainer = new StackPanel
        {
            Spacing = 16,
            MaxWidth = 680,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(24, 20)
        };

        if (showCompletion)
        {
            // 完成状态界面
            var completionCard = new Border
            {
                Padding = new Thickness(28),
                CornerRadius = new CornerRadius(12)
            };
            completionCard.Classes.Add("card");

            var compLayout = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };
            var isReviewCompletion = session.Mode == StudyMode.Review;
            var compTitle = new TextBlock
            {
                Text = isReviewCompletion
                    ? UiText.Bilingual("今日复习完成", "Today's review is complete")
                    : UiText.Bilingual("今日首次学习完成", "Today's first learning is complete"),
                FontSize = 26,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            compTitle.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PrimaryGreen"));
            compLayout.Children.Add(compTitle);

            compLayout.Children.Add(new TextBlock
            {
                Text = isReviewCompletion
                    ? UiText.Bilingual(
                        $"本轮 {round.Total} 个词汇已完成今日复习，学习进度已保存。",
                        $"{round.Total} words have completed today's review. Progress is saved.")
                    : UiText.Bilingual(
                        $"本批全部 {round.Total} 个词汇已完成首次学习，后续复习会按记忆表现安排。",
                        $"First learning is complete for all {round.Total} words. Later reviews follow your recall performance."),
                FontSize = 15,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            // 拼写强化准备：使用既有 PracticeSetupControl（唯一开始键），替代四个大按钮。
            var allBatchWords = (isReviewCompletion
                ? session.ReviewWords
                : session.BatchWords).ToList();
            var forgotBatchWords = _activeLearningPlan is null
                ? []
                : allBatchWords.Where(w => _activeLearningPlan.ForgotWordIds.Contains(w.Id)).ToList();

            var setup = new PracticeSetupControl(allBatchWords.Count, forgotBatchWords.Count,
                (onlyWeak, hints) => StartLearningTyping((onlyWeak ? forgotBatchWords : allBatchWords).Select(ToTypingWord).ToList(), hints),
                () => { });
            setup.Margin = new Thickness(0, 10, 0, 0);
            compLayout.Children.Add(setup);

            var hasNextPlan = HasNextQueuedLearningPlan();
            var finishButton = hasNextPlan
                ? LearningBilingualButton("继续下一个计划", "Continue to next plan",
                    AdvanceLearningPlanQueue, primary: true)
                : LearningBilingualButton("完成今日学习，返回计划概览",
                    "Finish today's study and return to plan overview", ExitDailyLearning, primary: true);
            finishButton.HorizontalAlignment = HorizontalAlignment.Center;
            finishButton.Margin = new Thickness(0, 10, 0, 0);
            compLayout.Children.Add(finishButton);

            completionCard.Child = compLayout;
            cardCenterContainer.Children.Add(completionCard);
        }
        else
        {
            // 正常学习中：与 focus 共享 StudyCanvasControl 内容组件。
            var word = _activeLearningPlan.Words.Single(w => w.Id == displayWordId);
            if (!_planRatedPending)
                PresentPlanMemory(_activeLearningPlan, round.Current, round.CurrentStep);

            var isLearnStep = !_planRatedPending && round.CurrentStep == StudyStep.Learn;

            // 模式与连击强化小标（与 focus 一致的进度表达）。
            var modeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var modeText = new TextBlock
            {
                Text = _planRatedPending
                    ? UiText.Bilingual("评分已保存 · 点击下一词继续", "Rating saved · continue to the next word")
                    : UiText.Text(isLearnStep ? "先学后测模式 · 请仔细阅读释义与例句" : "回忆卡测试"),
                FontSize = 13,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            modeText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
            modeRow.Children.Add(modeText);

            if (!isLearnStep && !_planRatedPending)
            {
                var streakStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                streakStack.Children.Add(BuildStreakDots(round.CurrentStreak, round.CurrentTarget));
                var streakText = new TextBlock
                {
                    Text = UiText.Format($"连击 {round.CurrentStreak}/{round.CurrentTarget}"),
                    FontSize = 12,
                    Opacity = 0.8,
                    VerticalAlignment = VerticalAlignment.Center
                };
                streakText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
                streakStack.Children.Add(streakText);
                Grid.SetColumn(streakStack, 1);
                modeRow.Children.Add(streakStack);
            }

            cardCenterContainer.Children.Add(modeRow);

            // 朗读：沿用现有 LocalWordAudioPlayer 读音机制（代码库无 SpeakWordAsync）。
            var pronounceRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            

            // 统一内容组件：纯色背景上的单词 / 音标 / 释义 / 定义 / 例句，全部 SelectableTextBlock。
            var planCanvas = CreateStudyCanvas();
            planCanvas.Speak=()=>GetLearningAudio().Play(word.Word,PlanWordAudioFile(word));
            planCanvas.Render(new StudyCanvasModel
            {
                Word = word.Word,
                Phonetic = string.IsNullOrWhiteSpace(word.Phonetic) ? "" : $"/{word.Phonetic.Trim('/')}/",
                Meaning = word.Meaning,
                Details = word.Definition,
                Example = word.Example,
                MeaningVisible = isLearnStep || _planAnswerVisible || _planRatedPending,
                Placeholder = UiText.Text("释义已隐藏，请在心中回忆词义")
            });
            cardCenterContainer.Children.Add(planCanvas);
        }

        contentScroll.Content = cardCenterContainer;
        Grid.SetRow(contentScroll, 1);
        _studyWorkspaceHost.Children.Add(contentScroll);

        // ==================== 3. 固定底部评分操作栏 (Grid.Row 2) ====================
        var bottomBorder = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        bottomBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("PaperBrush"));
        bottomBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));

        var bottomLayout = new StackPanel { Spacing = 8 };

        // 状态行
        var statusLine = new TextBlock
        {
            Text = _planRatedPending
                ? _planRatedWordCompleted
                    ? UiText.Bilingual("本词首学已完成并保存，等待进入下一词",
                        "First learning for this word is complete and saved; continue when ready")
                    : UiText.Bilingual("本次复习评分已保存，等待进入下一词",
                        "This review rating is saved; continue when ready")
                : UiText.Bilingual("进度已实时保存至计划", "Progress is saved to the plan in real time"),
            FontSize = 12,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        statusLine.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        bottomLayout.Children.Add(statusLine);

        // 操作按钮行
        var actionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        actionRow.SizeChanged += (_, _) =>
        {
            var narrow = actionRow.Bounds.Width < 640;
            actionRow.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            if (actionRow.Children.Count > 1)
            {
                var actions = actionRow.Children[^1];
                Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumn(actions, narrow ? 0 : 2);
                Grid.SetColumnSpan(actions, narrow ? 3 : 1);
                actions.HorizontalAlignment = HorizontalAlignment.Right;
                actions.Margin = new Thickness(0, narrow ? 8 : 0, 0, 0);
            }
        };

        if (!showCompletion)
        {
            // 左侧：撤销 / 改判 (保留撤销改判能力)
            var undoBox = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var btnUndo = LearningBilingualButton("撤销上一次", "Undo last rating", UndoDailyLearning);
            btnUndo.IsEnabled = session.CanUndo;
            undoBox.Children.Add(btnUndo);

            var btnRevise = LearningBilingualButton("改判为忘记", "Mark as forgotten",
                ReclassifyDailyLearningAsForgot);
            btnRevise.IsEnabled = session.CanUndo;
            undoBox.Children.Add(btnRevise);

            actionRow.Children.Add(undoBox);

            // 右侧动作
            var rightBox = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            Grid.SetColumn(rightBox, 2);

            if (_planRatedPending)
            {
                var btnNext = LearningBilingualButton(
                    $"下一词 ({ShortcutHint(ShortcutAction.Primary)})",
                    $"Next word ({ShortcutHint(ShortcutAction.Primary)})",
                    AdvanceDailyLearningAfterRating, primary: true);
                btnNext.Name = "StudyNextWord";
                rightBox.Children.Add(btnNext);
            }
            else if (round.CurrentStep == StudyStep.Learn)
            {
                // 先学后测主动作
                var btnStartRecall = LearningBilingualButton(
                    $"看完了，开始回忆 ({ShortcutHint(ShortcutAction.Primary)})",
                    $"Start recall ({ShortcutHint(ShortcutAction.Primary)})",
                    CompleteDailyLearn, primary: true);
                rightBox.Children.Add(btnStartRecall);
            }
            else if (!_planAnswerVisible)
            {
                // 尚未揭晓
                var btnRevealBottom = LearningBilingualButton(
                    $"揭晓释义 ({ShortcutHint(ShortcutAction.Primary)})",
                    $"Reveal meaning ({ShortcutHint(ShortcutAction.Primary)})", () =>
                    {
                        _planAnswerVisible = true;
                        RenderDailyLearning();
                    }, primary: true);
                rightBox.Children.Add(btnRevealBottom);
            }
            else
            {
                // 三键评分：中性次级按钮，等宽，统一主题中性
                var btnForgot = RatingButton(
                    UiText.Bilingual($"忘记 ({ShortcutHint(ShortcutAction.Forgot)})",
                        $"Forgot ({ShortcutHint(ShortcutAction.Forgot)})"), StudyRating.Forgot);
                var btnUnsure = RatingButton(
                    UiText.Bilingual($"模糊 ({ShortcutHint(ShortcutAction.Unsure)})",
                        $"Unsure ({ShortcutHint(ShortcutAction.Unsure)})"), StudyRating.Unsure);
                var btnKnown = RatingButton(
                    UiText.Bilingual($"认识 ({ShortcutHint(ShortcutAction.Known)})",
                        $"Known ({ShortcutHint(ShortcutAction.Known)})"), StudyRating.Known);
                btnForgot.Name = "StudyRateForgot";
                btnUnsure.Name = "StudyRateUnsure";
                btnKnown.Name = "StudyRateKnown";

                rightBox.Children.Add(btnForgot);
                rightBox.Children.Add(btnUnsure);
                rightBox.Children.Add(btnKnown);
            }

            actionRow.Children.Add(rightBox);
        }
        else
        {
            // 已完成批次
            var undo = LearningBilingualButton("撤销上一次", "Undo last rating", UndoDailyLearning);
            undo.IsEnabled = session.CanUndo; actionRow.Children.Add(undo);
            var btnReturnOverview = HasNextQueuedLearningPlan()
                ? LearningBilingualButton("继续下一个计划", "Continue to next plan",
                    AdvanceLearningPlanQueue, primary: true)
                : LearningBilingualButton("返回计划概览", "Return to plan overview",
                    ExitDailyLearning, primary: true);
            Grid.SetColumn(btnReturnOverview, 2);
            actionRow.Children.Add(btnReturnOverview);
        }

        bottomLayout.Children.Add(actionRow);
        bottomBorder.Child = bottomLayout;
        Grid.SetRow(bottomBorder, 2);
        _studyWorkspaceHost.Children.Add(bottomBorder);
    }

    private void CompleteDailyLearn()
    {
        if (_restoring || !_databaseAvailable || _dailyLearningSession == null) return;
        _dailyLearningSession.CompleteLearn();
        _planPresentation = null;
        PersistLearningSurface("plan");
        _planAnswerVisible = false;
        _planRatedPending = false;
        _planRatedWordId = null;
        _planRatedWordCompleted = false;
        RenderDailyLearning();
    }

    private void RateDailyLearning(StudyRating rating)
    {
        if (_restoring || !_databaseAvailable || _dailyLearningSession == null) return;
        if (_planRatedPending) return;
        if (_dailyLearningDate != LearningDay.TodayBjt(DateTime.UtcNow) && _activeLearningPlan != null)
        {
            StartDailyLearning(_activeLearningPlan, _dailyLearningSession.Mode, preserveQueue: true);
            SetStatus(UiText.Bilingual("日期已变化，今日计划卡片已刷新，请重新回忆。",
                "The date changed. Today's plan cards were refreshed; please recall again."));
            return;
        }

        _planStreakBefore = _dailyLearningSession.Round.CurrentStreak;
        _planRatingApplied = false;
        var ratedWordId = _dailyLearningSession.Round.Current;

        if (_dailyLearningSession.Rate(rating, () => SaveLearningPlans(_learningPlans)) is not { } result)
        {
            PersistLearningSurface("plan"); RenderDailyLearning();
            SetStatus(UiText.Bilingual("评分未能保存，已恢复原卡片，请重试。",
                "The rating could not be saved. The card was restored; try again.")); return;
        }

        _planRatedPending = true;
        _planRatedWordId = ratedWordId;
        _planRatedWordCompleted = result.Completed;
        _planAnswerVisible = true;
        PersistLearningSurface("plan");
        RenderDailyLearning();
        RenderLearningPlans();
        SetStatus(result.Completed
            ? UiText.Bilingual("评分已保存，本词首学完成。点击“下一词”继续。",
                "Rating saved. First learning for this word is complete. Select Next word to continue.")
            : UiText.Bilingual("评分已保存。点击“下一词”继续。",
                "Rating saved. Select Next word to continue."));
    }

    private void UndoDailyLearning()
    {
        if (_restoring || !_databaseAvailable || _dailyLearningSession == null) return;
        if (_dailyLearningSession.Undo(() => SaveLearningPlans(_learningPlans)))
        {
            _planRatedPending = false;
            _planRatedWordId = null;
            _planRatedWordCompleted = false;
            _planAnswerVisible = true;
            PersistLearningSurface("plan");
            RenderDailyLearning();
            RenderLearningPlans();
            SetStatus(UiText.Bilingual("已撤销上一次评分。", "The last rating was undone."));
        }
    }

    private void AdvanceDailyLearningAfterRating()
    {
        if (_restoring || !_databaseAvailable || _dailyLearningSession == null) return;
        if (!_planRatedPending)
        {
            RenderDailyLearning();
            return;
        }

        _planRatedPending = false;
        _planRatedWordId = null;
        _planRatedWordCompleted = false;
        _planAnswerVisible = false;
        _planPresentation = null;
        PersistLearningSurface("plan");
        RenderDailyLearning();
        RenderLearningPlans();
    }

    private void ReclassifyDailyLearningAsForgot()
    {
        if (_restoring || !_databaseAvailable || _dailyLearningSession == null || !_dailyLearningSession.CanUndo) return;
        // 先撤销上一作答恢复卡片，再直接提交“忘记了”
        if (_dailyLearningSession.Undo(() => SaveLearningPlans(_learningPlans)))
        {
            _planRatedPending = false;
            _planRatedWordId = null;
            _planRatedWordCompleted = false;
            _planAnswerVisible = false;
            RateDailyLearning(StudyRating.Forgot);
            SetStatus(UiText.Bilingual("已将上一次作答改判为「忘记」。",
                "The previous answer was marked as forgotten."));
        }
    }

    private static LearningWord ToTypingWord(DailyStudyPlanWord word) => new()
    {
        Id = word.Id,
        Words = [word.Word],
        Meaning = word.Meaning,
        Phonetic = word.Phonetic,
        Example = word.Example,
        AudioPath = word.AudioPath
    };

    private void AddBatchTypingButtons(StackPanel panel, string label, List<DailyStudyPlanWord> words)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(LearningBilingualText($"{label} · {words.Count} 词",
            $"{label} · {words.Count} words"));
        foreach (var hints in new[] { true })
        {
            var button = LearningBilingualButton("拼写练习", "Spelling practice",
                () => StartLearningTyping(words.Select(ToTypingWord).ToList(), hints));
            button.IsEnabled = words.Count > 0;
            row.Children.Add(button);
        }
        panel.Children.Add(row);
    }

    private void StartArchiveTyping(bool hints) =>
        StartLearningTyping(ArchivePlanWords().Select(ToTypingWord).ToList(), hints);

    private void StartLearningTyping(List<LearningWord> words, bool hints, string returnPage = "learning",
        string? returnPlanId = null)
    {
        ClearLearningPlanQueue();
        _typingReturnToPlanDetail = !string.IsNullOrWhiteSpace(returnPlanId);
        _typingReturnPlanId = returnPlanId;
        StartTypingSession(words, hints, tracksIeltsProgress: false, returnPage: returnPage,
            preserveReturnContext: true);
    }

    private void StartIeltsTyping(List<LearningWord> words, bool hints) =>
        StartTypingSession(words, hints, tracksIeltsProgress: true, returnPage: "ielts");

    private void StartExamTyping(List<LearningWord> words, bool hints) =>
        StartTypingSession(words, hints, tracksIeltsProgress: false, returnPage: "exams");

    private void StartTypingSession(List<LearningWord> words, bool hints, bool tracksIeltsProgress,
        string returnPage, bool preserveReturnContext = false)
    {
        if (words.Count == 0)
        {
            SetStatus(UiText.Bilingual("没有可训练的词汇，请先收藏或勾选词汇。",
                "There are no words available to practice. Add or select words first."));
            return;
        }

        if (!preserveReturnContext)
        {
            _typingReturnToPlanDetail = false;
            _typingReturnPlanId = null;
        }
        _learningTypingIeltsScope = tracksIeltsProgress;
        CancelLearningTypingTransition();
        StopWindowsLearningAudio();
        _learningTypingAnswerVisible = false;
        _learningTypingPhoneticVisible = false;
        _typingPreviousPage = NormalizeTypingReturnPage(returnPage);
        if (_currentPage != "learning") ShowPage("learning");

        _lastTypingWords = words;
        _learningTypingSession = new TypingSession();
        _learningTypingSession.Reset(words, hints);
        RenderLearningTyping();
        _typingHost!.IsVisible = true;
        SetGlobalFocusChrome(true);
    }

    private void ExitLearningTyping()
    {
        CancelLearningTypingTransition();
        StopWindowsLearningAudio();
        var returnPlanId = _typingReturnToPlanDetail ? _typingReturnPlanId : null;
        _learningTypingSession = null;
        _learningTypingIeltsScope = false;
        _typingReturnToPlanDetail = false;
        _typingReturnPlanId = null;
        _typingHost!.IsVisible = false;
        if (!string.IsNullOrWhiteSpace(returnPlanId) &&
            _learningPlans.FirstOrDefault(plan => plan.Id == returnPlanId) is { } returnPlan)
        {
            OpenLearningPlanDetails(returnPlan);
        }
        else if (_studyWorkspaceHost.IsVisible)
        {
            RenderDailyLearning();
        }
        else
        {
            RenderLearningPlans();
        }

        if (_typingPreviousPage != "learning") ShowPage(_typingPreviousPage);
        SetGlobalFocusChrome(false);
    }

    private Lexi.Features.Learning.SpellingPracticeControl? _spellingPractice;
    private void RenderLearningTyping()
    {
        CancelLearningTypingTransition();
        StopWindowsLearningAudio();
        var session=_learningTypingSession;
        if(session==null)return;
        if(session.Current==null)
        {
            var completed=new StackPanel {Spacing=16,MaxWidth=680,Margin=new Thickness(24),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            completed.Children.Add(LearningBilingualText("本轮完成", "Session complete", 26));
            completed.Children.Add(LearningBilingualText(
                $"练习 {session.Count} 词 · 重试 {session.Retries} 次 · 提示辅助 {session.AssistedCount}",
                $"{session.Count} words · {session.Retries} retries · {session.AssistedCount} assisted"));
            var errors=_lastTypingWords.Where(w=>session.ErrorIds.Contains(w.Id)).ToList();
            if(errors.Count>0)completed.Children.Add(LearningBilingualButton("再练错误词", "Practice missed words",()=>
                StartTypingSession(errors,session.Hints,_learningTypingIeltsScope,_typingPreviousPage,
                    preserveReturnContext:true),true));
            completed.Children.Add(LearningBilingualButton("返回", "Back", ExitLearningTyping));
            _typingHost!.Child=completed;_typingHost.IsVisible=true;return;
        }
        var word=session.Current;
        _spellingPractice=new Lexi.Features.Learning.SpellingPracticeControl {Name="SpellingPractice"};
        _learningTypingInput=_spellingPractice.InputBox;_learningTypingInput.Name="LearningTypingInput";
        _learningTypingHint=_spellingPractice.HintBlock;
        _learningTypingResult=_spellingPractice.FeedbackBlock;
        _learningTypingLetters=new StackPanel {IsVisible=false};
        _learningTypingStats=new TextBlock();
        _spellingPractice.OnSubmit=_=>SubmitLearningTyping();
        _spellingPractice.OnNext=SubmitLearningTyping;
        _spellingPractice.OnHint=()=>{session.RevealHint();UpdateSpellingSurface();};
        _spellingPractice.OnRevealAnswer=()=>
        {
            session.RevealAnswer();
            _learningTypingAnswerVisible=true;
            UpdateSpellingSurface();
        };
        _spellingPractice.OnCancel=ExitLearningTyping;
        _spellingPractice.OnSpeak=()=>ReplayLearningTypingWord();
        _spellingPractice.InputChanged+=HandleLearningTypingInput;
        _typingHost!.Child=_spellingPractice;_typingHost.IsVisible=true;
        UpdateTypingStats();UpdateSpellingSurface();
        Avalonia.Threading.Dispatcher.UIThread.Post(()=>
        {
            if(!ReferenceEquals(session,_learningTypingSession))return;
            _spellingPractice?.FocusInput();
            ReplayLearningTypingWord(showPhonetic:false);
        });
    }

    private static string NormalizeTypingReturnPage(string? page) =>
        page is "lookup" or "vocab" or "learning" or "ielts" or "exams" or "statistics"
            ? page : "learning";

    private void UpdateSpellingSurface()
    {
        var s=_learningTypingSession;if(s?.Current==null || _spellingPractice==null)return;
        var feedback = s.Outcome == TypingOutcome.Correct
            ? UiText.Bilingual("拼写正确！按 Enter 进入下一词。",
                "Correct! Press Enter to continue to the next word.")
            : s.AnswerViewed && _learningTypingAnswerVisible
                ? UiText.Bilingual("已显示答案，请照着输入后提交。",
                    "The answer is shown. Type it and submit to continue.")
                : s.Outcome switch
        {
            TypingOutcome.Retry => UiText.Bilingual("有错字，请修改后重试。",
                "There is a typo. Edit it and try again."),
            TypingOutcome.Pending when !string.IsNullOrWhiteSpace(s.Input) =>
                UiText.Bilingual("输入尚不完整，请继续输入。",
                    "The spelling is incomplete. Keep typing."),
            _ => ""
        };
        _spellingPractice.Update(new Lexi.Features.Learning.SpellingPracticeModel
        {
            Word=s.Current.Word,
            Input=s.Input,
            FailedInput=s.FailedInput,
            Hints=s.Hints,
            Outcome=s.Outcome,
            Meaning=string.IsNullOrWhiteSpace(s.Current.Meaning)?UiText.Bilingual("暂无释义","No meaning available"):s.Current.Meaning,
            Phonetic=_learningTypingPhoneticVisible?s.Current.Phonetic:"",
            Hint=s.HintText.Any(char.IsLetter)?s.HintText:UiText.Bilingual($"{s.Current.Word.Length} 个字母",$"{s.Current.Word.Length} letters"),
            Feedback=feedback,
            IsCorrect=s.Outcome==TypingOutcome.Correct?true:s.Outcome==TypingOutcome.Retry?false:null,
            AnswerRevealed=_learningTypingAnswerVisible && s.AnswerViewed,
            ProgressText=$"{s.Cursor}/{s.Count} · {UiText.Bilingual("重试","Retries")} {s.Retries} · {UiText.Bilingual("提示辅助","Assisted")} {s.AssistedCount}"
        });
    }

    private void HandleLearningTypingInput(string text)
    {
        var session=_learningTypingSession;
        if(session?.Current==null || session.Outcome==TypingOutcome.Correct)return;
        EvaluateLearningTypingInput(text);
    }

    private void SubmitLearningTyping()
    {
        if (_restoring || !_databaseAvailable)
        {
            SetStatus(UiText.Bilingual("词库正在恢复或不可用。",
                "The vocabulary archive is restoring or unavailable."));
            return;
        }
        var session = _learningTypingSession;
        if (session?.Current == null || _learningTypingInput == null) return;

        if (session.Outcome == TypingOutcome.Correct)
        {
            AdvanceLearningTyping();
            return;
        }

        EvaluateLearningTypingInput(_learningTypingInput.Text ?? "");
    }

    private void EvaluateLearningTypingInput(string text)
    {
        var session = _learningTypingSession;
        if (session?.Current == null) return;
        var wordId = session.Current.Id;
        var outcome = session.Submit(text);
        if (outcome == TypingOutcome.Correct)
        {
            if (_learningTypingIeltsScope)
            {
                _ieltsProgress.Typed.Add(wordId);
                SaveIeltsProgress();
            }
        }
        else if (outcome == TypingOutcome.Retry)
        {
            if (_learningTypingIeltsScope)
            {
                _ieltsProgress.Errors.Add(wordId);
                SaveIeltsProgress();
            }
        }

        UpdateTypingStats();

        if (outcome == TypingOutcome.Retry)
        {
            SystemFeedbackSound(false);
        }
        else if (outcome == TypingOutcome.Correct)
        {
            SystemFeedbackSound(true);
            ScheduleLearningTypingAdvance(session);
        }
        UpdateSpellingSurface();
    }

    private void ScheduleLearningTypingAdvance(TypingSession session)
    {
        CancelLearningTypingTransition();
        var cts=_typingAutoAdvanceCts=new CancellationTokenSource();
        _=AdvanceLearningTypingAfterDelayAsync(session,cts.Token);
    }

    private async Task AdvanceLearningTypingAfterDelayAsync(TypingSession session, CancellationToken token)
    {
        try
        {
            await Task.Delay(300,token);
            if(token.IsCancellationRequested)return;
            Avalonia.Threading.Dispatcher.UIThread.Post(()=>
            {
                if(!token.IsCancellationRequested && ReferenceEquals(session,_learningTypingSession) &&
                   session.Outcome==TypingOutcome.Correct)
                    AdvanceLearningTyping();
            });
        }
        catch(OperationCanceledException){}
    }

    private void AdvanceLearningTyping()
    {
        var session=_learningTypingSession;
        if(session?.Outcome!=TypingOutcome.Correct)return;
        CancelLearningTypingTransition();
        StopWindowsLearningAudio();
        _learningTypingAnswerVisible=false;
        _learningTypingPhoneticVisible=false;
        if(session.Advance())RenderLearningTyping();
    }

    private void ReplayLearningTypingWord(bool showPhonetic=true)
    {
        var word=_learningTypingSession?.Current;
        if(word==null)return;
        if(showPhonetic)_learningTypingPhoneticVisible=true;
        StopWindowsLearningAudio();
        GetLearningAudio().Play(word.Word,LearningWordAudioFile(word));
        if(showPhonetic)UpdateSpellingSurface();
    }

    private void CancelLearningTypingTransition()
    {
        var cts=_typingAutoAdvanceCts;
        _typingAutoAdvanceCts=null;
        if(cts==null)return;
        cts.Cancel();
        cts.Dispose();
    }

    private void UpdateTypingStats()
    {
        var s = _learningTypingSession;
        if (s != null && _learningTypingStats != null)
        {
            _learningTypingStats.Text = UiText.Bilingual(
                $"完成 {s.Cursor}/{s.Count} · 字符准确率 {s.Accuracy:F1}% · 重试 {s.Retries} 次 · 提示辅助 {s.AssistedCount} · {s.Wpm} WPM",
                $"{s.Cursor}/{s.Count} complete · {s.Accuracy:F1}% character accuracy · {s.Retries} retries · {s.AssistedCount} assisted · {s.Wpm} WPM");
        }
    }

    private void RenderTypingLetters(bool submitted)
    {
        var s = _learningTypingSession;
        if (s?.Current == null || _learningTypingLetters == null || _learningTypingInput == null) return;
        _learningTypingLetters.Children.Clear();

        var input = TypingSession.Normalize(_learningTypingInput.Text ?? "");
        // 输入中（含提示模式）不做即时错误着色，也不按真实答案泄露未输入字母。
        if (!submitted || s.Outcome == TypingOutcome.Pending)
        {
            foreach (var ch in input) _learningTypingLetters.Children.Add(LearningText(ch.ToString(), 23));
            return;
        }

        // 提交后仅按已输入内容逐字母判定，绝不泄露未输入的答案字母。
        foreach (var letter in TypingFeedbackModel.Build(TypingSession.Normalize(s.Current.Word), input, hints: false, s.Outcome))
        {
            var text = LearningText(letter.Character.ToString(), 23);
            if (letter.Tone == TypingLetterTone.Correct)
            {
                text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PrimaryGreen"));
            }
            else if (letter.Tone == TypingLetterTone.Wrong)
            {
                text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("DangerBrush"));
            }
            else
            {
                text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
            }
            _learningTypingLetters.Children.Add(text);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Lexi.Social;

namespace Lexi;

public partial class MainWindow
{
    private Grid? _statisticsPage;
    private ScrollViewer? _statisticsScroll;
    private Button? _statisticsNav;
    private readonly List<Button> _statisticsPeriodButtons = [];
    private StudyStatisticsSnapshot _statisticsSnapshot = new();
    private StatisticsHeatmap? _statisticsHeatmap;
    private StatisticsCurve? _statisticsCurve;
    private StatisticsPeriod _statisticsPeriod;
    private StatisticsActivityFilter _statisticsFilter;
    private int _statisticsYear = DateTime.Today.Year;
    private bool _statisticsConfiguring;
    private bool _statisticsExpandedWords;
    private bool _statisticsLifetimeRanking;
    private bool _statisticsExplanation;
    private bool _statisticsHistoryFailed;
    private bool _statisticsActivityFailed;
    private bool _statisticsRecovered;
    private IReadOnlyList<(double Days, double Rate)> _statisticsPrediction = [];
    private int _statisticsPredictionCount;

    private static string StatText(string chinese, string english) =>
        UiText.Language == "en" ? english : chinese;

    private static TextBlock StatLabel(string text, double size = 13, string color = "Muted")
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.38,
        };
        label.Bind(TextBlock.ForegroundProperty,
            new DynamicResourceExtension(color == "Ink" ? "InkBrush"
                : color == "Accent" ? "PrimaryGreen" : "MutedBrush"));
        return label;
    }

    private static string StatDuration(double seconds) => seconds <= 0
        ? StatText("暂无记录", "No record")
        : seconds >= 3600
            ? StatText($"{seconds / 3600:0.#} 小时", $"{seconds / 3600:0.#} h")
            : StatText($"{seconds / 60:0.#} 分钟", $"{seconds / 60:0.#} min");

    private static Border StatCard(Control child)
    {
        var card = new Border
        {
            Child = child,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22),
        };
        card.Bind(Border.BackgroundProperty, new DynamicResourceExtension("CardBrush"));
        card.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("LineBrush"));
        return card;
    }

    private static Button StatButton(string text, string? name = null)
    {
        var button = new Button
        {
            Content = text,
            Classes = { "secondary" },
            Padding = new Thickness(12, 7),
            FontSize = 12,
        };
        if (!string.IsNullOrWhiteSpace(name)) button.Name = name;
        return button;
    }

    private void InitializeStatistics()
    {
        if (_statisticsPage != null) return;
        var navHost = this.FindControl<StackPanel>("LearningNavHost");
        var pagesHost = this.FindControl<Panel>("PagesHost");
        if (navHost == null || pagesHost == null) return;

        _statisticsNav = new Button
        {
            Name = "NavStatistics",
            Content = BuildStatisticsNavigationContent(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        _statisticsNav.Classes.Add("nav");
        _statisticsNav.Click += (_, _) =>
        {
            if (FocusCanNavigate) ShowPage("statistics");
        };
        navHost.Children.Add(_statisticsNav);

        _statisticsPage = new Grid
        {
            Name = "StatisticsPage",
            IsVisible = false,
            Margin = new Thickness(30, 24, 30, 20),
        };
        pagesHost.Children.Add(_statisticsPage);
        _statisticsPage.PropertyChanged += (_, args) =>
        {
            if (args.Property == Visual.IsVisibleProperty && _statisticsPage?.IsVisible == false)
            {
                _statisticsHeatmap?.StopAnimation();
                _statisticsCurve?.StopAnimation();
            }
        };

        RenderStatisticsPage();
    }

    private Control BuildStatisticsNavigationContent()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 2,14 L 6,9 L 10,11 L 16,3 M 12,3 L 16,3 L 16,7"),
            Stroke = new SolidColorBrush(Color.Parse("#6F8794")),
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = StatText("学习统计", "Statistics"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private void SetStatisticsPageVisible(bool visible)
    {
        if (_statisticsPage == null) return;
        _statisticsPage.IsVisible = visible;
        if (!visible)
        {
            _statisticsHeatmap?.StopAnimation();
            _statisticsCurve?.StopAnimation();
        }
    }

    private void StatisticsEnterPage()
    {
        RefreshStatisticsPage();
        ReplayStatistics();
    }

    private void ReplayStatistics()
    {
        var reduced = ReduceMotionBox.IsChecked.GetValueOrDefault();
        _statisticsHeatmap?.Replay(reduced);
        _statisticsCurve?.Replay(reduced);
    }

    private void RefreshStatisticsLanguage()
    {
        if (_statisticsPage == null) return;
        if (_statisticsNav != null) _statisticsNav.Content = BuildStatisticsNavigationContent();
        RenderStatisticsPage();
        if (_currentPage == "statistics") ReplayStatistics();
    }

    private void RefreshStatisticsPage()
    {
        if (_statisticsPage == null) return;

        FlushStudyActivity(final: false);
        IReadOnlyList<CanonicalHistoryRow> rows = [];
        _statisticsHistoryFailed = false;
        try
        {
            if (_learningMemoryStore == null) Memory();
            if (_learningMemoryStore != null)
                rows = _learningMemoryStore.LoadCanonicalHistory(null, DateTime.UtcNow.AddTicks(1), null);
            else
                _statisticsHistoryFailed = true;
        }
        catch (Exception ex)
        {
            _statisticsHistoryFailed = true;
            Debug.WriteLine("[statistics] history unavailable: " + ex.GetType().Name);
        }

        var stored = _studyActivityStore?.Read()
            ?? new StudyActivityReadResult([], true, false, null);
        _statisticsActivityFailed = !stored.Writable;
        _statisticsRecovered = stored.Recovered;
        _statisticsSnapshot = StudyStatistics.Build(
            rows, stored.Activities, DateTime.UtcNow, TimeZoneInfo.Local);

        _statisticsPrediction = [];
        _statisticsPredictionCount = 0;
        if (!_statisticsHistoryFailed && _learningMemoryStore != null)
        {
            try
            {
                var cards = rows
                    .Select(row => row.WordKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
                    .Select(key => _learningMemoryStore.GetCard(key))
                    .Where(card => card != null && double.IsFinite(card.Stability) && card.Stability > 0)
                    .ToArray();
                var scheduler = _memoryParameterRuntime?.Service is { } personalization
                    ? new Fsrs6Scheduler(personalization.Weights)
                    : new Fsrs6Scheduler();
                _statisticsPredictionCount = cards.Length;
                _statisticsPrediction = SocialCurve
                    .Build(cards.Select(card => card!.Stability), scheduler.Retrievability)
                    .Select(point => (point.Days, point.Rate))
                    .ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[statistics] prediction unavailable: " + ex.GetType().Name);
            }
        }

        RenderStatisticsPage();
    }

    private void RenderStatisticsPage()
    {
        if (_statisticsPage == null) return;
        _statisticsConfiguring = true;
        var offset = _statisticsScroll?.Offset ?? default;
        _statisticsHeatmap?.StopAnimation();
        _statisticsCurve?.StopAnimation();
        _statisticsPeriodButtons.Clear();

        var body = new StackPanel
        {
            Spacing = 16,
            MaxWidth = 1080,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        body.Children.Add(BuildStatisticsHeader());
        if (_statisticsHistoryFailed || _statisticsActivityFailed)
            body.Children.Add(BuildStatisticsWarning());
        body.Children.Add(BuildStatisticsMetrics());
        body.Children.Add(BuildStatisticsPeaksCard());
        body.Children.Add(BuildStatisticsHeatmapCard());

        var comparison = new Grid();
        var curveCard = BuildStatisticsCurveCard();
        var wordsCard = BuildStatisticsWordsCard();
        comparison.Children.Add(curveCard);
        comparison.Children.Add(wordsCard);
        void LayoutComparison()
        {
            var wide = comparison.Bounds.Width >= 760;
            comparison.ColumnDefinitions = new ColumnDefinitions(wide ? "*,*" : "*");
            comparison.RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,Auto");
            comparison.ColumnSpacing = wide ? 16 : 0;
            comparison.RowSpacing = wide ? 0 : 16;
            Grid.SetRow(wordsCard, wide ? 0 : 1);
            Grid.SetColumn(wordsCard, wide ? 1 : 0);
        }
        LayoutComparison();
        comparison.SizeChanged += (_, _) => LayoutComparison();
        body.Children.Add(comparison);
        body.Children.Add(BuildStatisticsTrendCard());
        body.Children.Add(BuildStatisticsNotesCard());

        _statisticsScroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _statisticsPage.Children.Clear();
        _statisticsPage.Children.Add(_statisticsScroll);
        var currentScroll = _statisticsScroll;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_statisticsScroll, currentScroll)) currentScroll.Offset = offset;
        }, DispatcherPriority.Loaded);
        _statisticsConfiguring = false;
    }

    private Control BuildStatisticsHeader()
    {
        var layout = new StackPanel { Spacing = 6 };
        var title = StatLabel(StatText("学习统计", "Statistics"), 28, "Ink");
        title.FontWeight = FontWeight.SemiBold;
        layout.Children.Add(title);
        layout.Children.Add(StatLabel(
            StatText("基于有效学习记录 · 当前本地时区 · 时长从此版本开始记录",
                "Valid learning records · Current local time zone · Duration tracks from this version"),
            12));
        return layout;
    }

    private Control BuildStatisticsWarning()
    {
        var text = _statisticsHistoryFailed
            ? StatText("学习历史暂时无法读取；恢复后可重新进入本页。这里没有把读取失败当作零次学习。",
                "Learning history is unavailable. Reopen this page after recovery; missing data is not treated as zero study.")
            : _statisticsRecovered
                ? StatText("活动记录损坏，已读取最近完整备份。原文件保留，暂停写入；当前未保存时长仍保留在内存中。",
                    "Activity records were recovered from the latest complete backup. The original file is preserved and writing is paused; unsaved time remains in memory.")
                : StatText("学习时长暂时无法保存或读取；原记录已保留，未保存的时长仍保留在内存中。",
                    "Study duration cannot currently be saved or read. Existing records are preserved and unsaved time remains in memory.");
        var card = StatCard(StatLabel(text, 12));
        card.Bind(Border.BackgroundProperty, new DynamicResourceExtension("TintBrush"));
        return card;
    }

    private Control BuildStatisticsMetrics()
    {
        var metrics = new UniformGrid
        {
            Columns = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        metrics.Children.Add(StatCard(BuildStatisticsMetric(
            StatText("学习天数", "Study days"),
            _statisticsHistoryFailed ? "—" : _statisticsSnapshot.LearningDays.ToString(),
            StatText("有完成学习或有效练习的日期", "Days with completed learning or active practice"))));
        metrics.Children.Add(StatCard(BuildStatisticsMetric(
            StatText("已学词汇", "Learned words"),
            _statisticsHistoryFailed ? "—" : _statisticsSnapshot.LearnedWords.ToString(),
            StatText("按稳定词身份去重", "Unique stable word identities"))));
        metrics.Children.Add(StatCard(BuildStatisticsMetric(
            StatText("单词卡时长", "Flashcard time"),
            StatDuration(_statisticsSnapshot.CardSeconds),
            StatText($"初学 {StatDuration(_statisticsSnapshot.FirstLearnSeconds)} · 复习 {StatDuration(_statisticsSnapshot.ReviewSeconds)}",
                $"First learn {StatDuration(_statisticsSnapshot.FirstLearnSeconds)} · Review {StatDuration(_statisticsSnapshot.ReviewSeconds)}"))));
        metrics.Children.Add(StatCard(BuildStatisticsMetric(
            StatText("拼写时长", "Spelling time"),
            StatDuration(_statisticsSnapshot.SpellingSeconds),
            StatText("离开、最小化或闲置即暂停", "Paused when hidden, minimized or idle"))));
        foreach (var card in metrics.Children.OfType<Border>())
            card.Margin = new Thickness(4);
        metrics.SizeChanged += (_, _) =>
        {
            metrics.Columns = metrics.Bounds.Width >= 820 ? 4 : metrics.Bounds.Width >= 410 ? 2 : 1;
        };
        return new Border
        {
            Child = metrics,
            Margin = new Thickness(-4),
        };
    }

    private Control BuildStatisticsPeaksCard()
    {
        var peaks = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 30,
            LineSpacing = 12,
        };
        AddStatisticsPeak(peaks, StatText("单日词汇峰值", "Peak words / day"),
            _statisticsSnapshot.PeakWords.ToString());
        AddStatisticsPeak(peaks, StatText("单日时长峰值", "Peak time / day"),
            StatDuration(_statisticsSnapshot.PeakSeconds));
        AddStatisticsPeak(peaks, StatText("最长学习会话", "Longest session"),
            StatDuration(_statisticsSnapshot.LongestSessionSeconds));
        AddStatisticsPeak(peaks, StatText("最长连续", "Longest streak"),
            StatText($"{_statisticsSnapshot.LongestStreak} 天", $"{_statisticsSnapshot.LongestStreak} days"));
        AddStatisticsPeak(peaks, StatText("当前连续", "Current streak"),
            StatText($"{_statisticsSnapshot.CurrentStreak} 天", $"{_statisticsSnapshot.CurrentStreak} days"));
        return StatCard(peaks);
    }

    private Control BuildStatisticsMetric(string title, string value, string detail)
    {
        var stack = new StackPanel
        {
            Spacing = 5,
        };
        stack.Children.Add(StatLabel(title, 11));
        var number = StatLabel(value, 23, "Ink");
        number.FontWeight = FontWeight.SemiBold;
        number.TextWrapping = TextWrapping.NoWrap;
        number.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(number);
        var note = StatLabel(detail, 11);
        note.MaxHeight = 34;
        ToolTip.SetTip(note, detail);
        stack.Children.Add(note);
        stack.SizeChanged += (_, _) =>
        {
            var narrow = stack.Bounds.Width < 170;
            number.FontSize = narrow ? 17 : stack.Bounds.Width < 220 ? 20 : 23;
            number.LineHeight = number.FontSize * 1.22;
        };
        return stack;
    }

    private static void AddStatisticsPeak(Panel panel, string label, string value)
    {
        var stack = new StackPanel { Spacing = 2, MinWidth = 96 };
        stack.Children.Add(StatLabel(label, 10.5));
        stack.Children.Add(StatLabel(value, 14, "Ink"));
        panel.Children.Add(stack);
    }

    private Control BuildStatisticsHeatmapCard()
    {
        var panel = new StackPanel { Spacing = 13 };
        var heading = new StackPanel { Spacing = 4 };
        var title = StatLabel(StatText("学习活动", "Learning activity"), 17, "Ink");
        title.FontWeight = FontWeight.SemiBold;
        heading.Children.Add(title);
        heading.Children.Add(StatLabel(
            StatText("按日查看专注时长与学习节奏", "Review focused time and study rhythm by day"), 11));
        panel.Children.Add(heading);

        var controls = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 8,
        };
        foreach (var period in Enum.GetValues<StatisticsPeriod>())
        {
            var titleText = period switch
            {
                StatisticsPeriod.Day => StatText("每日", "Daily"),
                StatisticsPeriod.Week => StatText("每周", "Weekly"),
                StatisticsPeriod.Month => StatText("每月", "Monthly"),
                _ => StatText("累计", "Cumulative"),
            };
            var button = StatButton(titleText, "StatisticsPeriod" + period);
            button.Classes.Set("primary", period == _statisticsPeriod);
            button.Classes.Set("secondary", period != _statisticsPeriod);
            button.Click += (_, _) =>
            {
                _statisticsPeriod = period;
                RenderStatisticsPage();
                ReplayStatistics();
            };
            _statisticsPeriodButtons.Add(button);
            controls.Children.Add(button);
        }

        var years = _statisticsSnapshot.Days.Keys
            .Select(day => day.Year)
            .Append(DateTime.Today.Year)
            .Distinct()
            .OrderDescending()
            .ToArray();
        if (!years.Contains(_statisticsYear)) _statisticsYear = years[0];
        var year = new ComboBox
        {
            Name = "StatisticsYear",
            ItemsSource = years,
            SelectedItem = _statisticsYear,
            MinWidth = 92,
        };
        year.SelectionChanged += (_, _) =>
        {
            if (!_statisticsConfiguring && year.SelectedItem is int selected)
            {
                _statisticsYear = selected;
                RenderStatisticsPage();
                ReplayStatistics();
            }
        };
        controls.Children.Add(year);

        var filter = new ComboBox
        {
            Name = "StatisticsFilter",
            ItemsSource = new[]
            {
                StatText("全部练习", "All practice"),
                StatText("单词卡", "Flashcards"),
                StatText("拼写", "Spelling"),
            },
            SelectedIndex = (int)_statisticsFilter,
            MinWidth = 128,
        };
        filter.SelectionChanged += (_, _) =>
        {
            if (!_statisticsConfiguring)
            {
                _statisticsFilter = (StatisticsActivityFilter)Math.Max(0, filter.SelectedIndex);
                RenderStatisticsPage();
                ReplayStatistics();
            }
        };
        controls.Children.Add(filter);
        panel.Children.Add(controls);

        _statisticsHeatmap = new StatisticsHeatmap
        {
            Name = "StatisticsHeatmap",
            Snapshot = _statisticsSnapshot,
            Year = _statisticsYear,
            Period = _statisticsPeriod,
            Filter = _statisticsFilter,
            Text = StatText,
        };
        panel.Children.Add(_statisticsHeatmap);
        panel.Children.Add(StatLabel(_statisticsPeriod switch
        {
            StatisticsPeriod.Day => StatText(
                "每格对应一天；颜色越深，有效学习时间越多。悬停可查看初学、复习、拼写和新学词数。",
                "One square per day; darker colour means more active time. Hover for first learning, review, spelling and new words."),
            StatisticsPeriod.Week => StatText(
                "每列是一周，蓝格从底部向上表示该周专注时长。",
                "Each column is a week. Blue cells fill upward to show active time."),
            StatisticsPeriod.Month => StatText(
                "每月共享当月汇总与柱高，重复列只用于呈现月份宽度，不重复计时。",
                "Columns in one month share that month's total and height; repeated columns do not add time."),
            _ => StatText(
                "每列累计截至该周的年内专注时长；未来日期保持空白。",
                "Each column accumulates year-to-date active time through that week; future dates remain empty."),
        }, 11));
        return StatCard(panel);
    }

    private Control BuildStatisticsCurveCard()
    {
        var panel = new StackPanel { Spacing = 10 };
        var title = StatLabel(StatText("遗忘曲线", "Forgetting curve"), 17, "Ink");
        title.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(title);
        panel.Children.Add(StatLabel(
            StatText("模型预测与真实复习观察", "Model prediction and observed reviews"), 11));
        _statisticsCurve = new StatisticsCurve
        {
            Name = "StatisticsCurve",
            Prediction = _statisticsPrediction,
            Observations = _statisticsSnapshot.Observations,
            Text = StatText,
        };
        panel.Children.Add(_statisticsCurve);
        panel.Children.Add(StatLabel(
            _statisticsPredictionCount > 0
                ? StatText($"预测取 {_statisticsPredictionCount} 张当前词卡的平均值，假设不再学习。散点按真实复习间隔分组；悬停查看样本数。",
                    $"Prediction averages {_statisticsPredictionCount} current cards, assuming no further study. Dots group actual review gaps; hover for sample sizes.")
                : StatText("当前没有可用模型词卡；完成学习后才会生成预测。",
                    "No model cards are available yet. Predictions appear after completed learning."),
            11));

        var explain = StatButton(_statisticsExplanation
            ? StatText("收起参数解释", "Hide parameter explanation")
            : StatText("这条线怎么看 · 参数解释", "How to read this · Parameters"),
            "StatisticsCurveExplanation");
        explain.Click += (_, _) =>
        {
            _statisticsExplanation = !_statisticsExplanation;
            RenderStatisticsPage();
            ReplayStatistics();
        };
        panel.Children.Add(explain);
        if (_statisticsExplanation)
        {
            panel.Children.Add(StatLabel(StatText(
                "稳定性表示记忆预计能保持多久；难度描述模型对该词学习难度的判断；目标记住率为90%。曲线是预测，不是考试分数。",
                "Stability estimates how long a memory lasts; difficulty describes the model's judgment for this word; target retention is 90%. The curve is a prediction, not an exam score."),
                11));
        }
        return StatCard(panel);
    }

    private Control BuildStatisticsWordsCard()
    {
        var panel = new StackPanel { Spacing = 10 };
        var title = StatLabel(StatText("最易遗忘", "Most forgotten"), 17, "Ink");
        title.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(title);
        panel.Children.Add(StatLabel(
            StatText("只显示你的真实词汇，不生成示例排名", "Only your real words are shown; no example ranking is generated"), 11));
        var switcher = StatButton(_statisticsLifetimeRanking
            ? StatText("切换到近30天遗忘率", "Show 30-day forgetting rate")
            : StatText("切换到累计遗忘次数", "Show lifetime forgotten count"),
            "StatisticsRanking");
        switcher.HorizontalAlignment = HorizontalAlignment.Left;
        switcher.Click += (_, _) =>
        {
            _statisticsLifetimeRanking = !_statisticsLifetimeRanking;
            RenderStatisticsPage();
        };
        panel.Children.Add(switcher);

        var ranked = _statisticsLifetimeRanking
            ? _statisticsSnapshot.LifetimeForgettable.ToArray()
            : _statisticsSnapshot.Forgettable.ToArray();
        if (ranked.Length == 0)
            panel.Children.Add(StatLabel(StatText(
                "暂无足够记录；完成复习后会显示你的真实词汇。",
                "Not enough records yet. Your real words appear after reviews."), 12));
        foreach (var word in ranked.Take(_statisticsExpandedWords ? 50 : 8))
            panel.Children.Add(StatisticsWordRow(word));
        if (ranked.Length > 8)
        {
            var more = StatButton(_statisticsExpandedWords
                ? StatText("收起列表", "Show fewer")
                : StatText($"展开全部 {ranked.Length} 个词", $"Show all {ranked.Length} words"),
                "StatisticsExpandWords");
            more.HorizontalAlignment = HorizontalAlignment.Left;
            more.Click += (_, _) =>
            {
                _statisticsExpandedWords = !_statisticsExpandedWords;
                RenderStatisticsPage();
            };
            panel.Children.Add(more);
        }
        if (!_statisticsLifetimeRanking && _statisticsSnapshot.InsufficientWords.Count > 0)
        {
            panel.Children.Add(StatLabel(StatText(
                $"记录不足 · {_statisticsSnapshot.InsufficientWords.Count} 个词少于3次有效复习",
                $"Insufficient records · {_statisticsSnapshot.InsufficientWords.Count} words have fewer than three reviews"),
                12, "Ink"));
        }
        return StatCard(panel);
    }

    private Control StatisticsWordRow(ForgettableWord word)
    {
        var archive = _allWords.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.Archive.Uuid)
            && WordKeyResolver.FromArchive(candidate).Key == word.WordKey);
        var parsed = WordKey.Parse(word.WordKey);
        var label = archive?.Word
            ?? (parsed.Source == WordSource.Ielts
                ? _ieltsCatalog?.AllWords.FirstOrDefault(candidate => candidate.Id == parsed.SourceId)?.Word
                : null)
            ?? parsed.SourceId;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 2),
            ColumnSpacing = 10,
        };
        var button = StatButton(label);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Padding = new Thickness(8, 4);
        button.IsEnabled = archive != null;
        if (archive != null) button.Click += (_, _) => EnterArchiveFocusWithFirst(archive);
        ToolTip.SetTip(button, archive != null
            ? StatText("打开词汇卡片", "Open word card")
            : StatText("此词来自其他学习来源", "This word belongs to another learning source"));
        grid.Children.Add(button);
        var count = StatLabel(_statisticsLifetimeRanking
            ? StatText($"累计忘记 {word.AllForgotten} 次", $"Forgotten {word.AllForgotten} times total")
            : StatText($"{word.Rate:P0} · {word.Forgotten}/{word.Samples} 次复习",
                $"{word.Rate:P0} · {word.Forgotten}/{word.Samples} reviews"), 11);
        count.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(count, 1);
        grid.Children.Add(count);
        return grid;
    }

    private Control BuildStatisticsTrendCard()
    {
        var panel = new StackPanel { Spacing = 10 };
        var title = StatLabel(StatText("近14天学习时长", "Study time · last 14 days"), 17, "Ink");
        title.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(title);
        panel.Children.Add(StatLabel(StatText(
            "初学（浅蓝）／复习（深蓝）／拼写（中蓝）",
            "First learn (light blue) / Review (dark blue) / Spelling (medium blue)"), 11));
        panel.Children.Add(new StatisticsTimeTrend
        {
            Snapshot = _statisticsSnapshot,
            Text = StatText,
        });
        return StatCard(panel);
    }

    private Control BuildStatisticsNotesCard()
    {
        var panel = new StackPanel { Spacing = 7 };
        var title = StatLabel(StatText("数据口径", "What these numbers mean"), 15, "Ink");
        title.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(title);
        panel.Children.Add(StatLabel(StatText(
            $"总有效复习 {_statisticsSnapshot.Reviews} 次。新词初学不计为长期复习；改判使用最终有效结果，撤销记录不纳入。词数按稳定词身份去重，不跨词书合并同形词。",
            $"{_statisticsSnapshot.Reviews} valid reviews. First learning is excluded from long-term review counts; revisions use the final valid result and undone records are excluded. Words use stable identity without merging matching spellings across books."), 11));
        panel.Children.Add(StatLabel(StatText(
            "时长只记录可见、活动窗口中的单词卡或拼写练习。60秒无按键、点击或滚动即暂停；休眠间隔不计时。旧版未记录时长的日期显示“暂无记录”，不会补零。",
            "Time counts flashcards or spelling in a visible, active window. It pauses after 60 seconds without a key, click or scroll; sleep gaps are excluded. Dates without duration in older versions show no record rather than zero."), 11));
        return StatCard(panel);
    }
}

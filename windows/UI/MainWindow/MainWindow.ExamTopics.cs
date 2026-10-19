using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lexi;

public partial class MainWindow
{
    private const string ExamAllUnitsId = "__all__";
    private static readonly string[] ExamUiTopicIds =
        new[] { "ielts" }.Concat(ExamCatalog.TopicIds).ToArray();

    private Border? _examTopicsPage;
    private Button? _examTopicsNav;
    private Grid? _examTopicsLayout;
    private Grid? _examWorkspace;
    private Grid? _examTopicNavigationGrid;
    private Border? _examTopicNavigationBorder;
    private StackPanel? _examTopicNavigationPanel;
    private ScrollViewer? _examTopicNavigationScroll;
    private TextBlock? _examTopicsNavTitle;
    private TextBlock? _examTopicsNavHint;
    private Button? _examTopicsHomeBtn;
    private Grid? _examTopicsBody;
    private Grid? _examSelectorGrid;
    private Border? _examSelectorSurface;
    private Grid? _examTopicsFooter;
    private Border? _examEmptyPanel;
    private readonly Dictionary<string, Button> _examTopicButtons = new(StringComparer.Ordinal);
    private ComboBox? _examBookList;
    private ComboBox? _examUnitList;
    private ListBox? _examWordList;
    private Control? _examBookColumnControl;
    private Control? _examUnitColumnControl;
    private Control? _examWordColumnControl;
    private TextBlock? _examBookColumnTitle;
    private TextBlock? _examUnitColumnTitle;
    private TextBlock? _examWordColumnTitle;
    private Grid? _examWordHeader;
    private WrapPanel? _examWordToolbar;
    private Button? _examSelectAllBtn;
    private Button? _examClearWordsBtn;
    private WrapPanel? _examFooterActionHost;
    private TextBlock? _examSelectionSummary;
    private TextBlock? _examEmptyState;
    private TextBlock? _examTopicTitle;
    private TextBlock? _examTopicSubtitle;
    private Button? _examCreatePlanBtn;
    private Button? _examStartCardsBtn;
    private Button? _examStartHintsBtn;
    private Button? _examStartDictationBtn;
    private string _examSelectedTopicId = "ielts";
    private string? _examSelectedBookId;
    private string? _examSelectedSectionId;
    private readonly HashSet<string> _examSelectedWordIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExamSelectionState> _examSelectionsByBook = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _examBookByTopic = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _examSectionByBook = new(StringComparer.Ordinal);
    private bool _examInitialSettingsHadBookContext;
    private bool _examSelectionStatesHydrated;
    private bool _ieltsSelectionStateHydrated;
    private bool _examTopicsLanguageHooked;
    private bool _examTopicsRendering;
    private ExamTopicsLayoutMode _examTopicsLayoutMode = ExamTopicsLayoutMode.Medium;
    private GridLength _examGlobalNavigationColumnWidth = new(208);

    private enum ExamTopicsLayoutMode
    {
        Narrow,
        Medium,
        Wide
    }

    private sealed class ExamSelectionState
    {
        public HashSet<string> SelectedWordIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> InitializedSectionIds { get; } = new(StringComparer.Ordinal);
    }

    private sealed record ExamChoice(string Id, string Display, object? Payload = null)
    {
        public override string ToString() => Display;
    }

    /// <summary>自包含初始化：导航入口、页面容器、语言刷新挂钩。主窗口构造期调用一次。</summary>
    private void InitializeExamTopics()
    {
        var nav = new Button
        {
            Name = "NavExamTopics",
            Content = UiText.Bilingual("考试专题", "Exam topics"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        nav.Classes.Add("nav");
        nav.Click += (_, _) => OpenExamTopics();
        this.FindControl<StackPanel>("LearningNavHost")?.Children.Add(nav);
        _examTopicsNav = nav;

        var page = BuildExamTopicsPage();
        this.FindControl<Panel>("PagesHost")?.Children.Add(page);
        page.IsVisible = false;
        page.SizeChanged += (_, _) => UpdateExamTopicsResponsiveLayout(compact: false);
        if (MainBodyGrid.ColumnDefinitions.Count > 0)
            _examGlobalNavigationColumnWidth = MainBodyGrid.ColumnDefinitions[0].Width;

        if (!_examTopicsLanguageHooked)
        {
            _examTopicsLanguageHooked = true;
            LanguageToggleBtn.Click += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshExamTopicsStaticText();
                    foreach (var (topicId, button) in _examTopicButtons)
                        button.Content = ExamTopicDisplay(topicId);
                });
                if (_examTopicsPage?.IsVisible == true)
                    Dispatcher.UIThread.Post(RefreshExamTopicsPage);
            };
            SettingsLanguageCombo.SelectionChanged += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshExamTopicsStaticText();
                    foreach (var (topicId, button) in _examTopicButtons)
                        button.Content = ExamTopicDisplay(topicId);
                });
                if (_examTopicsPage?.IsVisible == true)
                    Dispatcher.UIThread.Post(RefreshExamTopicsPage);
            };
        }
        RestoreExamSelectionFromSettings();
    }

    private void SetExamTopicsPageVisible(bool visible)
    {
        if (_examTopicsPage == null) return;
        _examTopicsPage.IsVisible = visible;
        _examTopicsNav?.Classes.Set("active", visible);
        ApplyExamTopicsNavigationContext(visible);
        if (visible)
        {
            RefreshExamTopicsPage();
            // Settings restoration re-shows the main sidebar after this method returns.
            // Reapply the exam context on the next UI turn so closing Settings on this
            // page cannot leave both navigation rails visible.
            Dispatcher.UIThread.Post(() =>
                ApplyExamTopicsNavigationContext(_examTopicsPage?.IsVisible == true));
        }
    }

    /// <summary>
    /// The exam page owns the left navigation rail while it is open. The main learning
    /// sidebar and its grid column are suppressed together so no blank 208px gutter remains.
    /// </summary>
    private void ApplyExamTopicsNavigationContext(bool active)
    {
        if (_globalFocusActive) return;

        SidebarBorder.IsVisible = !active;
        if (MainBodyGrid.ColumnDefinitions.Count > 0)
        {
            MainBodyGrid.ColumnDefinitions[0].Width = active
                ? new GridLength(0)
                : _examGlobalNavigationColumnWidth;
        }
    }

    /// <summary>
    /// Three explicit breakpoints adjust density, but every supported width keeps the
    /// topic rail on the left so the page has one stable navigation model.
    /// </summary>
    private void UpdateExamTopicsResponsiveLayout(bool compact)
    {
        var windowWidth = Bounds.Width > 0 ? Bounds.Width : Width;
        var mode = windowWidth >= 1280
            ? ExamTopicsLayoutMode.Wide
            : compact || windowWidth < 960
                ? ExamTopicsLayoutMode.Narrow
                : ExamTopicsLayoutMode.Medium;
        ApplyExamTopicsResponsiveLayout(mode);
    }

    private void ApplyExamTopicsResponsiveLayout(ExamTopicsLayoutMode mode)
    {
        _examTopicsLayoutMode = mode;
        var narrow = mode == ExamTopicsLayoutMode.Narrow;
        var wide = mode == ExamTopicsLayoutMode.Wide;

        if (_examTopicsLayout != null)
        {
            _examTopicsLayout.ColumnDefinitions = new ColumnDefinitions(
                narrow ? "156,*" : wide ? "208,*" : "184,*");
            _examTopicsLayout.RowDefinitions = new RowDefinitions("*");
            _examTopicsLayout.ColumnSpacing = narrow ? 12 : wide ? 24 : 18;
            _examTopicsLayout.RowSpacing = 0;
            _examTopicsLayout.Margin = narrow
                ? new Thickness(14, 16, 14, 12)
                : wide
                    ? new Thickness(28, 24, 28, 16)
                    : new Thickness(22, 20, 22, 14);
        }

        if (_examTopicNavigationGrid != null)
        {
            _examTopicNavigationGrid.RowDefinitions = new RowDefinitions("Auto,Auto,*");
            _examTopicNavigationGrid.RowSpacing = narrow ? 6 : 10;
        }
        if (_examTopicsNavTitle != null)
        {
            _examTopicsNavTitle.FontSize = narrow ? 16 : wide ? 21 : 19;
            _examTopicsNavTitle.Margin = narrow ? new Thickness(2, 0, 0, 0) : default;
        }
        if (_examTopicsHomeBtn != null)
        {
            _examTopicsHomeBtn.Padding = narrow ? new Thickness(9, 7) : new Thickness(12, 9);
            _examTopicsHomeBtn.MinHeight = narrow ? 34 : 38;
        }
        if (_examTopicsNavHint != null) _examTopicsNavHint.IsVisible = !narrow;
        if (_examTopicNavigationPanel != null)
        {
            _examTopicNavigationPanel.Orientation = Orientation.Vertical;
            _examTopicNavigationPanel.Spacing = 3;
        }
        if (_examTopicNavigationScroll != null)
        {
            Grid.SetRow(_examTopicNavigationScroll, 2);
            _examTopicNavigationScroll.HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            _examTopicNavigationScroll.VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        }
        if (_examTopicNavigationBorder != null)
        {
            _examTopicNavigationBorder.Padding = narrow
                ? new Thickness(0, 0, 12, 0)
                : new Thickness(0, 0, wide ? 18 : 14, 0);
            _examTopicNavigationBorder.BorderThickness = new Thickness(0, 0, 1, 0);
        }
        foreach (var button in _examTopicButtons.Values)
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = narrow ? new Thickness(9, 7) : new Thickness(12, 9);
            button.MinHeight = narrow ? 34 : 38;
        }

        if (_examTopicNavigationBorder != null)
        {
            Grid.SetRow(_examTopicNavigationBorder, 0);
            Grid.SetColumn(_examTopicNavigationBorder, 0);
            Grid.SetRowSpan(_examTopicNavigationBorder, 1);
            Grid.SetColumnSpan(_examTopicNavigationBorder, 1);
        }
        if (_examWorkspace != null)
        {
            Grid.SetRow(_examWorkspace, 0);
            Grid.SetColumn(_examWorkspace, 1);
        }

        if (_examTopicTitle != null)
            _examTopicTitle.FontSize = narrow ? 22 : wide ? 28 : 25;
        if (_examWorkspace != null) _examWorkspace.RowSpacing = narrow ? 10 : 14;
        if (_examSelectorGrid != null)
        {
            _examSelectorGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
            _examSelectorGrid.RowDefinitions = new RowDefinitions("Auto");
            _examSelectorGrid.ColumnSpacing = narrow ? 10 : 14;
            _examSelectorGrid.RowSpacing = 0;
        }
        PlaceInGrid(_examBookColumnControl, row: 0, column: 0, columnSpan: 1);
        PlaceInGrid(_examUnitColumnControl, row: 0, column: 1, columnSpan: 1);
        PlaceInGrid(_examWordColumnControl, row: 1, column: 0, columnSpan: 2);
        if (_examSelectorSurface != null)
            _examSelectorSurface.Padding = narrow ? new Thickness(10) : new Thickness(12);

        if (_examWordHeader != null)
        {
            _examWordHeader.ColumnDefinitions = narrow
                ? new ColumnDefinitions("*")
                : new ColumnDefinitions("*,Auto");
            _examWordHeader.RowDefinitions = narrow
                ? new RowDefinitions("Auto,Auto")
                : new RowDefinitions("Auto");
            _examWordHeader.ColumnSpacing = 10;
            _examWordHeader.RowSpacing = narrow ? 8 : 0;
        }
        if (_examWordColumnTitle != null)
        {
            Grid.SetRow(_examWordColumnTitle, 0);
            Grid.SetColumn(_examWordColumnTitle, 0);
        }
        if (_examWordToolbar != null)
        {
            Grid.SetRow(_examWordToolbar, narrow ? 1 : 0);
            Grid.SetColumn(_examWordToolbar, narrow ? 0 : 1);
        }

        var stackedFooter = !wide;
        if (_examSelectionSummary != null)
        {
            Grid.SetRow(_examSelectionSummary, 0);
            Grid.SetColumn(_examSelectionSummary, 0);
            Grid.SetColumnSpan(_examSelectionSummary, stackedFooter ? 1 : 1);
        }
        if (_examFooterActionHost != null)
        {
            Grid.SetRow(_examFooterActionHost, stackedFooter ? 1 : 0);
            Grid.SetColumn(_examFooterActionHost, stackedFooter ? 0 : 1);
            _examFooterActionHost.HorizontalAlignment = HorizontalAlignment.Left;
        }
        if (_examTopicsFooter != null)
        {
            _examTopicsFooter.ColumnDefinitions = stackedFooter
                ? new ColumnDefinitions("*")
                : new ColumnDefinitions("*,Auto");
            _examTopicsFooter.RowDefinitions = stackedFooter
                ? new RowDefinitions("Auto,Auto")
                : new RowDefinitions("Auto");
            _examTopicsFooter.RowSpacing = stackedFooter ? 8 : 0;
        }
    }

    private static void PlaceInGrid(Control? control, int row, int column, int columnSpan)
    {
        if (control == null) return;
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, columnSpan);
    }

    private void OpenExamTopics()
    {
        ShowPage("exams");
        SetExamTopicsPageVisible(true);
    }

    private void RefreshExamTopicsPage()
    {
        if (_examTopicsPage == null) return;
        _examTopicsRendering = true;
        try
        {
            RefreshExamTopicsStaticText();
            if (!ExamUiTopicIds.Contains(_examSelectedTopicId, StringComparer.Ordinal))
                _examSelectedTopicId = "ielts";
            foreach (var (topicId, button) in _examTopicButtons)
            {
                button.Content = ExamTopicDisplay(topicId);
                button.Classes.Set("active", topicId == _examSelectedTopicId);
            }
            if (_examTopicTitle != null)
            {
                _examTopicTitle.Text = UiText.Bilingual(
                    $"{ExamTopicDisplay(_examSelectedTopicId)} 学习专题",
                    $"{ExamTopicDisplay(_examSelectedTopicId)} study topic");
            }
            RenderExamBooks();
            RenderExamUnits();
            EnsureCurrentExamSectionInitialized();
            RenderExamWords();
            if (_examTopicSubtitle != null)
            {
                var book = CurrentExamBook();
                var section = CurrentExamSection();
                var bookCount = book?.AllWords.Count() ?? 0;
                var sectionCount = section?.Entries.Count ?? bookCount;
                var bookName = book == null ? UiText.Bilingual("未选择词书", "No book selected") : ExamBookDisplay(book);
                var sectionName = section == null
                    ? UiText.Bilingual("整本词书", "Entire book")
                    : ExamSectionDisplay(section);
                _examTopicSubtitle.Text = UiText.Bilingual(
                    $"{bookName} · {sectionName} · 当前章节 {sectionCount} 词，词书共 {bookCount} 词。",
                    $"{bookName} · {sectionName} · {sectionCount} words in this section, {bookCount} in the book.");
            }
        }
        finally
        {
            _examTopicsRendering = false;
        }
        UpdateExamActions();
        UpdateExamTopicsResponsiveLayout(compact: false);
    }

    private void RefreshExamTopicsStaticText()
    {
        if (_examTopicsNav != null)
            _examTopicsNav.Content = UiText.Bilingual("考试专题", "Exam topics");
        if (_examTopicsNavTitle != null)
            _examTopicsNavTitle.Text = UiText.Bilingual("考试专题", "Exam topics");
        if (_examTopicsHomeBtn != null)
            _examTopicsHomeBtn.Content = UiText.Bilingual("← 返回主页", "← Back to Home");
        if (_examTopicsNavHint != null)
            _examTopicsNavHint.Text = UiText.Bilingual("选择一个小专题开始学习。", "Choose a topic to start studying.");
        if (_examBookColumnTitle != null)
            _examBookColumnTitle.Text = UiText.Bilingual("词书", "Books");
        if (_examUnitColumnTitle != null)
            _examUnitColumnTitle.Text = UiText.Bilingual("章节 / 整本", "Sections / whole book");
        if (_examBookList != null)
            _examBookList.PlaceholderText = UiText.Bilingual("选择词书", "Choose a book");
        if (_examUnitList != null)
            _examUnitList.PlaceholderText = UiText.Bilingual("选择章节", "Choose a section");
        if (_examWordColumnTitle != null)
            _examWordColumnTitle.Text = UiText.Bilingual("词条（可多选）", "Entries (multi-select)");
        if (_examSelectAllBtn != null)
            _examSelectAllBtn.Content = UiText.Bilingual("全选当前", "Select current");
        if (_examClearWordsBtn != null)
            _examClearWordsBtn.Content = UiText.Bilingual("清空当前", "Clear current");
        if (_examStartCardsBtn != null)
            _examStartCardsBtn.Content = UiText.Bilingual("单词卡认词", "Word cards");
        if (_examStartHintsBtn != null)
            _examStartHintsBtn.Content = UiText.Bilingual("淡写", "Hinted spelling");
        if (_examStartDictationBtn != null)
            _examStartDictationBtn.Content = UiText.Bilingual("默写", "Dictation");
        if (_examCreatePlanBtn != null)
            _examCreatePlanBtn.Content = UiText.Bilingual("用选中词条创建计划", "Create plan from selected");
    }

    private Border BuildExamTopicsPage()
    {
        if (_examTopicsPage != null) return _examTopicsPage;

        var root = new Border
        {
            Name = "ExamTopicsPage",
            IsVisible = false,
            Background = Brushes.Transparent
        };
        var layout = new Grid
        {
            Name = "ExamTopicsLayout",
            ColumnDefinitions = new ColumnDefinitions("208,*"),
            ColumnSpacing = 24,
            Margin = new Thickness(28, 24, 28, 16)
        };
        _examTopicsLayout = layout;

        var navigation = BuildExamTopicNavigation();
        Grid.SetColumn(navigation, 0);
        layout.Children.Add(navigation);

        var workspace = new Grid
        {
            Name = "ExamTopicsWorkspace",
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14
        };
        _examWorkspace = workspace;

        var header = new StackPanel { Spacing = 5 };
        _examTopicTitle = new TextBlock
        {
            Name = "ExamTopicWorkspaceTitle",
            Text = UiText.Bilingual($"{ExamTopicDisplay(_examSelectedTopicId)} 学习专题",
                $"{ExamTopicDisplay(_examSelectedTopicId)} study topic"),
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        header.Children.Add(_examTopicTitle);
        _examTopicSubtitle = new TextBlock
        {
            Name = "ExamTopicWorkspaceSubtitle",
            Text = UiText.Bilingual(
                "按词书与章节选择词条；首次进入章节会并入该章节全部词条。",
                "Choose a book and section. A section is added in full the first time it is opened."),
            FontSize = 13,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        };
        header.Children.Add(_examTopicSubtitle);
        Grid.SetRow(header, 0);
        workspace.Children.Add(header);

        var body = new Grid
        {
            Name = "ExamTopicsBody",
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 16
        };
        _examTopicsBody = body;

        var selectorSurface = new Border
        {
            Name = "ExamTopicSelectors",
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
        selectorSurface.Bind(Border.BackgroundProperty, this.GetResourceObservable("InsetBrush"));
        selectorSurface.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        _examSelectorSurface = selectorSurface;

        var selectorGrid = new Grid
        {
            Name = "ExamTopicSelectorGrid",
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 14
        };
        _examSelectorGrid = selectorGrid;
        _examBookColumnControl = BuildExamBookColumn();
        _examUnitColumnControl = BuildExamUnitColumn();
        _examWordColumnControl = BuildExamWordColumn();
        Grid.SetRow(_examBookColumnControl, 0);
        Grid.SetColumn(_examBookColumnControl, 0);
        Grid.SetRow(_examUnitColumnControl, 0);
        Grid.SetColumn(_examUnitColumnControl, 1);
        selectorGrid.Children.Add(_examBookColumnControl);
        selectorGrid.Children.Add(_examUnitColumnControl);
        selectorSurface.Child = selectorGrid;
        Grid.SetRow(selectorSurface, 0);
        Grid.SetColumn(selectorSurface, 0);
        Grid.SetColumnSpan(selectorSurface, 2);
        body.Children.Add(selectorSurface);

        Grid.SetRow(_examWordColumnControl, 1);
        Grid.SetColumn(_examWordColumnControl, 0);
        Grid.SetColumnSpan(_examWordColumnControl, 2);
        body.Children.Add(_examWordColumnControl);

        var bodyHost = new Grid();
        bodyHost.Children.Add(body);
        var emptyPanel = new Border
        {
            Name = "ExamEmptyPanel",
            IsVisible = false,
            Padding = new Thickness(24),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
        emptyPanel.Bind(Border.BackgroundProperty, this.GetResourceObservable("InsetBrush"));
        emptyPanel.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        _examEmptyState = new TextBlock
        {
            Name = "ExamEmptyState",
            FontSize = 14,
            Opacity = 0.78,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        emptyPanel.Child = _examEmptyState;
        _examEmptyPanel = emptyPanel;
        bodyHost.Children.Add(emptyPanel);
        Grid.SetRow(bodyHost, 1);
        workspace.Children.Add(bodyHost);

        var footer = new Border
        {
            Padding = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        var footerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _examTopicsFooter = footerGrid;
        _examSelectionSummary = new TextBlock
        {
            Name = "ExamSelectionSummary",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        footerGrid.Children.Add(_examSelectionSummary);

        // Wraps instead of clipping: four bilingual actions do not fit on one line at 760px.
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        _examStartCardsBtn = ExamButton("单词卡认词", "Word cards",
            StartExamCards, false, "ExamStartCardsBtn");
        _examStartHintsBtn = ExamButton("淡写", "Hinted spelling",
            () => StartExamLearning(hints: true), false, "ExamStartHintsBtn");
        _examStartDictationBtn = ExamButton("默写", "Dictation",
            () => StartExamLearning(hints: false), false, "ExamStartDictationBtn");
        _examCreatePlanBtn = ExamButton("用选中词条创建计划", "Create plan from selected",
            OpenExamPlanCreator, true, "ExamCreatePlanBtn");
        _examStartCardsBtn.Margin = new Thickness(0, 0, 10, 0);
        _examStartHintsBtn.Margin = new Thickness(0, 0, 10, 0);
        _examStartDictationBtn.Margin = new Thickness(0, 0, 10, 0);
        actions.Children.Add(_examStartCardsBtn);
        actions.Children.Add(_examStartHintsBtn);
        actions.Children.Add(_examStartDictationBtn);
        actions.Children.Add(_examCreatePlanBtn);
        _examFooterActionHost = actions;
        Grid.SetColumn(actions, 1);
        footerGrid.Children.Add(actions);
        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        workspace.Children.Add(footer);

        Grid.SetColumn(workspace, 1);
        layout.Children.Add(workspace);

        root.Child = layout;
        _examTopicsPage = root;
        return root;
    }

    private Control BuildExamTopicNavigation()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10
        };
        _examTopicNavigationGrid = panel;
        _examTopicsNavTitle = new TextBlock
        {
            Name = "ExamTopicsTitle",
            Text = UiText.Bilingual("考试专题", "Exam topics"),
            FontSize = 21,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var heading = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        heading.Children.Add(_examTopicsNavTitle);

        _examTopicsHomeBtn = new Button
        {
            Name = "ExamTopicsHome",
            Content = UiText.Bilingual("← 返回主页", "← Back to Home"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 9),
            MinHeight = 38
        };
        _examTopicsHomeBtn.Classes.Add("nav");
        _examTopicsHomeBtn.Click += (_, _) => ShowPage(NavigationState.FallbackPage);
        heading.Children.Add(_examTopicsHomeBtn);
        panel.Children.Add(heading);

        _examTopicsNavHint = new TextBlock
        {
            Name = "ExamTopicsHint",
            Text = UiText.Bilingual("选择一个小专题开始学习。", "Choose a topic to start studying."),
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_examTopicsNavHint, 1);
        panel.Children.Add(_examTopicsNavHint);

        var topics = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };
        _examTopicNavigationPanel = topics;
        foreach (var topicId in ExamUiTopicIds)
        {
            var captured = topicId;
            var button = new Button
            {
                Name = "ExamTopic_" + topicId,
                Content = ExamTopicDisplay(topicId),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 9),
                MinHeight = 38
            };
            button.Classes.Add("nav");
            button.Click += (_, _) => OpenExamTopic(captured);
            _examTopicButtons[topicId] = button;
            topics.Children.Add(button);
        }
        var scroll = new ScrollViewer
        {
            Content = topics,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        _examTopicNavigationScroll = scroll;
        Grid.SetRow(scroll, 2);
        panel.Children.Add(scroll);

        var border = new Border
        {
            Name = "ExamTopicNavigation",
            Padding = new Thickness(0, 0, 18, 0),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Background = Brushes.Transparent
        };
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        _examTopicNavigationBorder = border;
        border.Child = panel;
        return border;
    }

    private Control BuildExamBookColumn()
    {
        _examBookList = new ComboBox
        {
            Name = "ExamBookList",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 38,
            MaxDropDownHeight = 320,
            PlaceholderText = UiText.Bilingual("选择词书", "Choose a book")
        };
        _examBookList.SelectionChanged += (_, _) =>
        {
            if (_examTopicsRendering) return;
            var choice = _examBookList?.SelectedItem as ExamChoice;
            if (choice == null || choice.Id == _examSelectedBookId) return;
            CommitActiveExamSelection();
            _examSelectedBookId = choice.Id;
            _examSelectedSectionId = _examSectionByBook.GetValueOrDefault(choice.Id);
            _examBookByTopic[_examSelectedTopicId] = choice.Id;
            ActivateExamSelectionState();
            RenderExamUnits();
            EnsureCurrentExamSectionInitialized();
            RenderExamWords();
            UpdateExamActions();
            PersistExamSelection();
        };
        return ExamSelectorColumn("词书", "Books", _examBookList, out _examBookColumnTitle);
    }

    private Control BuildExamUnitColumn()
    {
        _examUnitList = new ComboBox
        {
            Name = "ExamUnitList",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 38,
            MaxDropDownHeight = 320,
            PlaceholderText = UiText.Bilingual("选择章节", "Choose a section")
        };
        _examUnitList.SelectionChanged += (_, _) =>
        {
            if (_examTopicsRendering) return;
            var choice = _examUnitList?.SelectedItem as ExamChoice;
            if (choice == null || choice.Id == _examSelectedSectionId) return;
            _examSelectedSectionId = choice.Id;
            var book = CurrentExamBook();
            if (book != null) _examSectionByBook[book.Id] = choice.Id;
            EnsureCurrentExamSectionInitialized();
            RenderExamWords();
            UpdateExamActions();
            PersistExamSelection();
        };
        return ExamSelectorColumn("章节 / 整本", "Sections / whole book", _examUnitList, out _examUnitColumnTitle);
    }

    private Control BuildExamWordColumn()
    {
        var stack = new Grid
        {
            Name = "ExamWordSection",
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 12
        };
        var header = new Grid
        {
            Name = "ExamWordHeader",
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        _examWordHeader = header;
        _examWordColumnTitle = new TextBlock
        {
            Text = UiText.Bilingual("词条（可多选）", "Entries (multi-select)"),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        header.Children.Add(_examWordColumnTitle);

        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        _examWordToolbar = toolbar;
        _examSelectAllBtn = ExamButton("全选当前", "Select current", SelectAllExamWords, false, "ExamSelectAllBtn");
        _examClearWordsBtn = ExamButton("清空当前", "Clear current", ClearExamWords, false, "ExamClearWordsBtn");
        _examClearWordsBtn.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(_examSelectAllBtn);
        toolbar.Children.Add(_examClearWordsBtn);
        Grid.SetColumn(toolbar, 1);
        header.Children.Add(toolbar);
        Grid.SetRow(header, 0);
        stack.Children.Add(header);

        _examWordList = new ListBox
        {
            Name = "ExamWordList",
            SelectionMode = SelectionMode.Multiple,
            AutoScrollToSelectedItem = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<ExamChoice>((choice, _) =>
                choice == null ? new Control() : BuildExamWordCard(choice))
        };
        _examWordList.SelectionChanged += (_, _) =>
        {
            if (_examTopicsRendering) return;
            SyncExamWordSelection();
            UpdateExamActions();
            PersistExamSelection();
        };
        Grid.SetRow(_examWordList, 1);
        stack.Children.Add(_examWordList);
        return stack;
    }

    private Control BuildExamWordCard(ExamChoice choice)
    {
        if (choice.Payload is not LearningWord word)
            return new TextBlock { Text = choice.Display, TextWrapping = TextWrapping.Wrap };

        var content = new StackPanel { Spacing = 4 };
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = new TextBlock
        {
            Text = word.Word,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(title);
        var speak = new Lexi.Controls.InlineAudioButton(
            UiText.Bilingual("朗读单词", "Listen to word"),
            () => GetLearningAudio().Play(word.Word, LearningWordAudioFile(word)))
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, -4, -4, 0)
        };
        Grid.SetColumn(speak, 1);
        titleRow.Children.Add(speak);
        content.Children.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(word.Phonetic))
        {
            content.Children.Add(new TextBlock
            {
                Text = word.Phonetic,
                FontSize = 12,
                Opacity = 0.68,
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (!string.IsNullOrWhiteSpace(word.Meaning))
        {
            content.Children.Add(new TextBlock
            {
                Text = word.Meaning,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (!string.IsNullOrWhiteSpace(word.Example))
        {
            content.Children.Add(new TextBlock
            {
                Text = word.Example,
                FontSize = 12,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var border = new Border
        {
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = content
        };
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBrush"));
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        return border;
    }

    /// <summary>
    /// A selector is only a label plus a list. The surrounding selector strip owns
    /// the surface, which avoids the old card-inside-card hierarchy.
    /// </summary>
    private Grid ExamSelectorColumn(string chinese, string english, Control content, out TextBlock title)
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 7 };
        title = new TextBlock
        {
            Text = UiText.Bilingual(chinese, english),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.76
        };
        panel.Children.Add(title);
        Grid.SetRow(content, 1);
        panel.Children.Add(content);
        return panel;
    }

    private Button ExamButton(string chinese, string english, Action action, bool primary, string name)
    {
        var button = new Button
        {
            Name = name,
            Content = UiText.Bilingual(chinese, english),
            Padding = new Thickness(14, 8)
        };
        button.Classes.Add(primary ? "primary" : "secondary");
        button.Click += (_, _) =>
        {
            if (_restoring || !_databaseAvailable)
            {
                SetStatus(UiText.Bilingual("词库正在恢复或不可用，请稍后再试。", "The library is restoring or unavailable; try again later."));
                return;
            }
            try { action(); }
            catch (Exception ex)
            {
                SetStatus(UiText.Language == "en"
                    ? "Exam topic action failed. Please try again."
                    : UiText.Bilingual("考试专题操作失败：", "Exam topic action failed: ") + ex.Message);
            }
        };
        return button;
    }

    private void OpenExamTopic(string topicId)
    {
        if (!ExamUiTopicIds.Contains(topicId, StringComparer.Ordinal)) return;
        CommitActiveExamSelection();
        _examSelectedTopicId = topicId;
        _examSelectedBookId = _examBookByTopic.GetValueOrDefault(topicId);
        _examSelectedSectionId = _examSelectedBookId == null
            ? null
            : _examSectionByBook.GetValueOrDefault(_examSelectedBookId);
        OpenExamTopics();
        EnsureCurrentExamSectionInitialized();
        RenderExamWords();
        UpdateExamActions();
        PersistExamSelection();
    }

    private void RenderExamBooks()
    {
        if (_examBookList == null || _examUnitList == null || _examWordList == null) return;
        var previousRendering = _examTopicsRendering;
        _examTopicsRendering = true;
        try
        {
            var books = ExamBooksFor(_examSelectedTopicId);
            if (books.Count == 0)
            {
                _examBookList.ItemsSource = null;
                _examUnitList.ItemsSource = null;
                _examWordList.ItemsSource = null;
                ShowExamEmpty(_examSelectedTopicId == "ielts"
                    ? UiText.Bilingual("IELTS 资料暂不可用，其他考试词书仍可使用。",
                        "IELTS resources are unavailable; other exam books still work.")
                    : UiText.Bilingual("考试词书目录暂不可用，本地学习不受影响。",
                        "The exam book catalog is unavailable; local learning is unaffected."));
                return;
            }

            HydrateExamSelectionStates(books);
            if (_examSelectedBookId == null || books.All(b => b.Id != _examSelectedBookId))
                _examSelectedBookId = _examBookByTopic.GetValueOrDefault(_examSelectedTopicId);
            if (_examSelectedBookId == null || books.All(b => b.Id != _examSelectedBookId))
                _examSelectedBookId = books[0].Id;
            _examBookByTopic[_examSelectedTopicId] = _examSelectedBookId;
            var choices = books.Select(b => new ExamChoice(b.Id, ExamBookDisplay(b), b)).ToList();
            _examBookList.ItemsSource = choices;
            _examBookList.SelectedItem = choices.First(c => c.Id == _examSelectedBookId);
            ActivateExamSelectionState();
            ShowExamBody();
        }
        finally
        {
            _examTopicsRendering = previousRendering;
        }
    }

    private void RenderExamUnits()
    {
        if (_examUnitList == null || _examWordList == null) return;
        var previousRendering = _examTopicsRendering;
        _examTopicsRendering = true;
        try
        {
            var book = CurrentExamBook();
            if (book == null)
            {
                _examUnitList.ItemsSource = null;
                _examWordList.ItemsSource = null;
                return;
            }

            var allWords = book.AllWords.ToList();
            var choices = new List<ExamChoice>
            {
                new(ExamAllUnitsId,
                    UiText.Bilingual($"整本词书 · {allWords.Count} 词", $"Entire book · {allWords.Count} words"),
                    null)
            };
            choices.AddRange(book.Sections.Select(s => new ExamChoice(
                s.Id,
                $"{ExamSectionDisplay(s)} · {s.Entries.Count}",
                s)));
            if (_examSelectedSectionId == null || choices.All(c => c.Id != _examSelectedSectionId))
                _examSelectedSectionId = _examSectionByBook.GetValueOrDefault(book.Id);
            if (_examSelectedSectionId == null || choices.All(c => c.Id != _examSelectedSectionId))
                _examSelectedSectionId = book.Sections.FirstOrDefault()?.Id ?? ExamAllUnitsId;
            _examSectionByBook[book.Id] = _examSelectedSectionId;
            _examUnitList.ItemsSource = choices;
            _examUnitList.SelectedItem = choices.First(c => c.Id == _examSelectedSectionId);
        }
        finally
        {
            _examTopicsRendering = previousRendering;
        }
    }

    private void RenderExamWords()
    {
        if (_examWordList == null) return;
        var previousRendering = _examTopicsRendering;
        _examTopicsRendering = true;
        try
        {
            var words = CurrentExamWords();
            var choices = words.Select(w => new ExamChoice(w.Id, ExamWordDisplay(w), w)).ToList();
            _examWordList.ItemsSource = choices;
            if (_examWordList.SelectedItems is { } selected)
            {
                selected.Clear();
                foreach (var choice in choices.Where(c => _examSelectedWordIds.Contains(c.Id)))
                    selected.Add(choice);
            }
        }
        finally
        {
            _examTopicsRendering = previousRendering;
        }
    }

    private List<ExamBook> ExamBooksFor(string topicId)
    {
        if (topicId == "ielts")
        {
            // IELTS keeps every local kind here: 22 vocabulary chapters plus listening 179,
            // the three reading-synonym units and the spelling chapter. Entries added by the
            // 20 exam books carry the "exam:" prefix and belong to their own books instead.
            var sections = TryEnsureIeltsCatalog()?.Sections
                .Where(s => !s.Kind.StartsWith("exam:", StringComparison.Ordinal))
                .ToList() ?? [];
            if (sections.Count == 0) return [];
            return
            [
                new ExamBook
                {
                    TopicId = "ielts",
                    Id = "ielts",
                    BookId = "ielts",
                    Title = "IELTS 教材",
                    EnglishTitle = "IELTS course",
                    Sections = sections
                }
            ];
        }
        return EnsureExamCatalog()?.BooksForTopic(topicId) ?? [];
    }

    private ExamBook? CurrentExamBook()
    {
        var books = ExamBooksFor(_examSelectedTopicId);
        return books.FirstOrDefault(b => b.Id == _examSelectedBookId) ?? books.FirstOrDefault();
    }

    private LearningSection? CurrentExamSection()
    {
        if (_examSelectedSectionId == null || _examSelectedSectionId == ExamAllUnitsId) return null;
        return CurrentExamBook()?.Sections.FirstOrDefault(s => s.Id == _examSelectedSectionId);
    }

    private List<LearningWord> CurrentExamWords()
    {
        var book = CurrentExamBook();
        if (book == null) return [];
        return CurrentExamSection()?.Entries.ToList() ?? book.AllWords.ToList();
    }

    private List<LearningWord> SelectedExamWords()
    {
        var book = CurrentExamBook();
        if (book == null) return [];
        return book.AllWords.Where(w => _examSelectedWordIds.Contains(w.Id)).ToList();
    }

    private void SyncExamWordSelection()
    {
        if (_examWordList?.ItemsSource is not IEnumerable<ExamChoice> choices) return;
        var visible = choices.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var state = CurrentExamSelectionState();
        state.SelectedWordIds.ExceptWith(visible);
        if (_examWordList.SelectedItems is { } selected)
        {
            foreach (var choice in selected.OfType<ExamChoice>())
            {
                _examSelectedWordIds.Add(choice.Id);
                state.SelectedWordIds.Add(choice.Id);
            }
        }
        _examSelectedWordIds.Clear();
        _examSelectedWordIds.UnionWith(state.SelectedWordIds);
    }

    private void SelectAllExamWords()
    {
        var state = CurrentExamSelectionState();
        foreach (var word in CurrentExamWords())
        {
            _examSelectedWordIds.Add(word.Id);
            state.SelectedWordIds.Add(word.Id);
        }
        RenderExamWords();
        UpdateExamActions();
        PersistExamSelection();
    }

    private void ClearExamWords()
    {
        var state = CurrentExamSelectionState();
        state.InitializedSectionIds.Add(_examSelectedSectionId ?? ExamAllUnitsId);
        if (_examWordList?.ItemsSource is IEnumerable<ExamChoice> choices)
        {
            var visible = choices.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            _examSelectedWordIds.ExceptWith(visible);
            state.SelectedWordIds.ExceptWith(visible);
        }
        RenderExamWords();
        UpdateExamActions();
        PersistExamSelection();
    }

    private void RestoreExamSelectionFromSettings()
    {
        _examSelectedTopicId = ExamUiTopicIds.Contains(_settings.ExamTopicId, StringComparer.Ordinal)
            ? _settings.ExamTopicId
            : "ielts";
        _examSelectedBookId = string.IsNullOrWhiteSpace(_settings.ExamBookId)
            ? null
            : _settings.ExamBookId.Trim();
        _examSelectedSectionId = string.IsNullOrWhiteSpace(_settings.ExamSectionId)
            ? null
            : _settings.ExamSectionId.Trim();
        _examInitialSettingsHadBookContext = !string.IsNullOrWhiteSpace(_settings.ExamBookId);
        _examSelectedWordIds.Clear();
        foreach (var wordId in _settings.ExamSelectedWordIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(wordId))
                _examSelectedWordIds.Add(wordId.Trim());
        }
    }

    private void HydrateExamSelectionStates(IReadOnlyList<ExamBook> currentBooks)
    {
        var persisted = _settings.ExamSelectedWordIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var catalog = EnsureExamCatalog();
        if (!_examSelectionStatesHydrated && catalog != null)
        {
            _examSelectionStatesHydrated = true;
            foreach (var wordId in persisted)
            {
                var book = catalog.FindBookByWordId(wordId);
                if (book == null) continue;
                var state = GetOrCreateExamSelectionState(book.Id);
                if (book.AllWords.Any(word => word.Id == wordId))
                    state.SelectedWordIds.Add(wordId);
            }

            foreach (var book in catalog.Books)
            {
                var state = GetOrCreateExamSelectionState(book.Id);
                if (state.SelectedWordIds.Count > 0)
                    _examBookByTopic.TryAdd(book.TopicId, book.Id);
                foreach (var section in book.Sections)
                {
                    if (section.Entries.Any(word => state.SelectedWordIds.Contains(word.Id)))
                        state.InitializedSectionIds.Add(section.Id);
                }
            }
        }

        // IELTS is a local catalog alongside the six exam-book catalogs. Hydrate it
        // independently so a persisted IELTS selection is not dropped when the first
        // visited topic is CET-4, GRE, or another exam catalog.
        if (!_ieltsSelectionStateHydrated)
        {
            var ieltsBook = currentBooks.FirstOrDefault(book => book.Id == "ielts")
                ?? ExamBooksFor("ielts").FirstOrDefault();
            if (ieltsBook != null)
            {
                _ieltsSelectionStateHydrated = true;
                var state = GetOrCreateExamSelectionState(ieltsBook.Id);
                foreach (var section in ieltsBook.Sections)
                {
                    foreach (var word in section.Entries)
                    {
                        if (persisted.Contains(word.Id))
                            state.SelectedWordIds.Add(word.Id);
                    }
                    if (section.Entries.Any(word => state.SelectedWordIds.Contains(word.Id)))
                        state.InitializedSectionIds.Add(section.Id);
                }
                if (state.SelectedWordIds.Count > 0)
                    _examBookByTopic.TryAdd("ielts", ieltsBook.Id);
            }
        }

        var currentBook = currentBooks.FirstOrDefault(book => book.Id == _examSelectedBookId);
        if (currentBook != null)
        {
            var state = GetOrCreateExamSelectionState(currentBook.Id);
            if (_examInitialSettingsHadBookContext && !string.IsNullOrWhiteSpace(_examSelectedSectionId))
                state.InitializedSectionIds.Add(_examSelectedSectionId);
            _examBookByTopic[_examSelectedTopicId] = currentBook.Id;
            if (!string.IsNullOrWhiteSpace(_examSelectedSectionId))
                _examSectionByBook[currentBook.Id] = _examSelectedSectionId;
        }
    }

    private ExamSelectionState CurrentExamSelectionState()
    {
        var book = CurrentExamBook();
        return GetOrCreateExamSelectionState(book?.Id ?? "__current__");
    }

    private ExamSelectionState GetOrCreateExamSelectionState(string bookId)
    {
        if (!_examSelectionsByBook.TryGetValue(bookId, out var state))
        {
            state = new ExamSelectionState();
            _examSelectionsByBook[bookId] = state;
        }
        return state;
    }

    private void ActivateExamSelectionState()
    {
        var state = CurrentExamSelectionState();
        _examSelectedWordIds.Clear();
        _examSelectedWordIds.UnionWith(state.SelectedWordIds);
    }

    private void CommitActiveExamSelection()
    {
        var book = CurrentExamBook();
        if (book == null) return;
        var state = GetOrCreateExamSelectionState(book.Id);
        var bookWordIds = book.AllWords.Select(word => word.Id).ToHashSet(StringComparer.Ordinal);
        state.SelectedWordIds.Clear();
        state.SelectedWordIds.UnionWith(_examSelectedWordIds.Where(bookWordIds.Contains));
    }

    private void EnsureCurrentExamSectionInitialized()
    {
        var book = CurrentExamBook();
        if (book == null) return;
        var sectionId = _examSelectedSectionId ?? ExamAllUnitsId;
        _examSectionByBook[book.Id] = sectionId;
        var state = GetOrCreateExamSelectionState(book.Id);
        var firstVisit = state.InitializedSectionIds.Add(sectionId);
        if (!firstVisit) return;

        foreach (var word in CurrentExamWords())
        {
            state.SelectedWordIds.Add(word.Id);
            _examSelectedWordIds.Add(word.Id);
        }
    }

    private void PersistExamSelection()
    {
        if (_restoring || !_databaseAvailable) return;
        CommitActiveExamSelection();
        _settings.ExamTopicId = _examSelectedTopicId;
        _settings.ExamBookId = _examSelectedBookId;
        _settings.ExamSectionId = _examSelectedSectionId;
        _settings.ExamSelectedWordIds = _examSelectionsByBook.Values
            .SelectMany(state => state.SelectedWordIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        try
        {
            _vocabService.SaveExamSelection(
                _settings.ExamTopicId,
                _settings.ExamBookId,
                _settings.ExamSectionId,
                _settings.ExamSelectedWordIds);
        }
        catch (Exception ex)
        {
            SetStatus(UiText.Language == "en"
                ? "Could not save the exam selection. Please try again."
                : UiText.Bilingual("考试专题选择未能保存：", "Could not save the exam selection: ")
                  + ex.Message);
        }
    }

    private void StartExamLearning(bool hints)
    {
        var words = SelectedExamWords();
        if (words.Count == 0)
        {
            SetStatus(UiText.Bilingual("请先选择至少一个词条。", "Select at least one entry first."));
            return;
        }
        StartExamTyping(words, hints);
    }

    private void StartExamCards()
    {
        var words = SelectedExamWords();
        if (words.Count == 0)
        {
            SetStatus(UiText.Bilingual("请先选择至少一个词条。", "Select at least one entry first."));
            return;
        }
        EnterExamFocus(words);
    }

    private void OpenExamPlanCreator()
    {
        var book = CurrentExamBook();
        var words = SelectedExamWords();
        if (book == null || words.Count == 0)
        {
            SetStatus(UiText.Bilingual("请先选择词书和词条。", "Choose a book and at least one entry first."));
            return;
        }
        OpenPlanCreator(
            DailyStudyPlanSource.Ielts,
            CurrentExamSection(),
            book,
            words,
            wholeBook: string.Equals(_examSelectedSectionId, ExamAllUnitsId, StringComparison.Ordinal));
    }

    private void UpdateExamActions()
    {
        var words = SelectedExamWords();
        var count = words.Count;
        if (_examSelectionSummary != null)
            _examSelectionSummary.Text = count == 0
                ? UiText.Bilingual("尚未选择词条。", "No entries selected.")
                : UiText.Bilingual($"已选 {count} 个词条。", $"{count} entries selected.");
        if (_examCreatePlanBtn != null) _examCreatePlanBtn.IsEnabled = count > 0;
        if (_examStartCardsBtn != null) _examStartCardsBtn.IsEnabled = count > 0;
        if (_examStartHintsBtn != null) _examStartHintsBtn.IsEnabled = count > 0;
        if (_examStartDictationBtn != null) _examStartDictationBtn.IsEnabled = count > 0;
    }

    private void ShowExamEmpty(string message)
    {
        if (_examTopicsBody != null) _examTopicsBody.IsVisible = false;
        if (_examEmptyPanel != null) _examEmptyPanel.IsVisible = true;
        if (_examEmptyState != null) _examEmptyState.Text = message;
    }

    private void ShowExamBody()
    {
        if (_examTopicsBody != null) _examTopicsBody.IsVisible = true;
        if (_examEmptyPanel != null) _examEmptyPanel.IsVisible = false;
    }

    private static string ExamTopicDisplay(string topicId) => topicId switch
    {
        "ielts" => "IELTS",
        "cet4" => UiText.Bilingual("四级", "CET-4"),
        "cet6" => UiText.Bilingual("六级", "CET-6"),
        "kaoyan" => UiText.Bilingual("考研", "Postgraduate"),
        "gmat" => "GMAT",
        "toefl" => "TOEFL",
        "gre" => "GRE",
        _ => topicId
    };

    private static string ExamBookDisplay(ExamBook book) =>
        UiText.Bilingual(book.Title, string.IsNullOrWhiteSpace(book.EnglishTitle) ? "Book" : book.EnglishTitle);

    private static string ExamSectionDisplay(LearningSection section) =>
        UiText.Bilingual(section.Title, string.IsNullOrWhiteSpace(section.EnglishTitle) ? "Section" : section.EnglishTitle);

    private static string ExamWordDisplay(LearningWord word)
    {
        var phonetic = string.IsNullOrWhiteSpace(word.Phonetic) ? "" : "  " + word.Phonetic;
        var meaning = string.IsNullOrWhiteSpace(word.Meaning) ? "" : "   " + word.Meaning;
        return word.Word + phonetic + meaning;
    }

    private static string ExamPlanSourceLabel(ExamBook book, IReadOnlyList<LearningSection> sections,
        IReadOnlyCollection<string> selectedIds)
    {
        var bookTitle = ExamBookDisplay(book);
        var ids = selectedIds as HashSet<string> ?? selectedIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count > 0 && book.AllWords.All(w => ids.Contains(w.Id)))
            return bookTitle + " · " + UiText.Bilingual("整本词书", "Entire book");
        var matched = sections.Where(s => s.Entries.Any(e => ids.Contains(e.Id))).ToList();
        if (matched.Count == 1 && ids.Count == matched[0].Entries.Count &&
            matched[0].Entries.All(e => ids.Contains(e.Id)))
            return bookTitle + " · " + ExamSectionDisplay(matched[0]);
        return bookTitle + " · " + UiText.Bilingual("选中词", "Selected words");
    }

    private static bool IsExamPlan(DailyStudyPlan plan) =>
        plan.Source == DailyStudyPlanSource.Ielts && plan.Words.Any(w => ExamCatalog.IsExamWordId(w.Id));

    private static string? PlanWordAudioFile(DailyStudyPlanWord word) =>
        ExamCatalog.IsExamWordId(word.Id) ? null : IeltsCatalog.ResolveAsset(word.AudioPath);

    private static string? LearningWordAudioFile(LearningWord word) =>
        ExamCatalog.IsExamWordId(word.Id) ? null : IeltsCatalog.ResolveAsset(word.AudioPath);

    private static List<(string PlanName, string Word)> FindExamPlanOverlaps(
        IEnumerable<DailyStudyPlan> plans, DailyStudyPlan draft)
    {
        var ids = draft.Words.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        return plans
            .Where(p => p.Status == DailyStudyPlanStatus.Active &&
                        p.Source == draft.Source && p.Id != draft.Id)
            .SelectMany(p => p.Words.Where(w => ids.Contains(w.Id))
                .Select(w => (PlanName: p.Name, Word: w.Word)))
            .ToList();
    }

    private string PlanSourceDisplay(DailyStudyPlan plan)
    {
        if (plan.Source != DailyStudyPlanSource.Ielts)
            return UiText.Bilingual("词汇档案", "Vocabulary archive");
        if (!IsExamPlan(plan))
            return string.IsNullOrWhiteSpace(plan.SourceLabel)
                ? UiText.Bilingual("IELTS 专题", "IELTS")
                : UiText.Redisplay(plan.SourceLabel);

        var catalog = EnsureExamCatalog();
        var ids = plan.Words
            .Select(word => word.Id)
            .Where(ExamCatalog.IsExamWordId)
            .ToHashSet(StringComparer.Ordinal);
        var books = ids
            .Select(id => catalog?.FindBookByWordId(id))
            .Where(book => book != null)
            .Cast<ExamBook>()
            .DistinctBy(book => book.Id)
            .ToList();
        if (books.Count == 0)
            return string.IsNullOrWhiteSpace(plan.SourceLabel)
                ? UiText.Bilingual("考试词书", "Exam book")
                : UiText.Redisplay(plan.SourceLabel);

        if (books.Count > 1)
        {
            var topicCount = books.Select(book => book.TopicId).Distinct(StringComparer.Ordinal).Count();
            var sectionCount = books
                .SelectMany(book => book.Sections)
                .Where(section => section.Entries.Any(entry => ids.Contains(entry.Id)))
                .Select(section => section.Id)
                .Distinct(StringComparer.Ordinal)
                .Count();
            return UiText.Bilingual($"{topicCount} 个考试专题", $"{topicCount} exam topics") + " · " +
                   UiText.Bilingual($"{books.Count} 本词书", $"{books.Count} books") + " · " +
                   UiText.Bilingual($"{sectionCount} 个章节", $"{sectionCount} sections");
        }

        var selectedBook = books[0];
        if (ids.Count == selectedBook.AllWords.Count() &&
            selectedBook.AllWords.All(word => ids.Contains(word.Id)))
        {
            return ExamTopicDisplay(selectedBook.TopicId) + " · " +
                   ExamBookDisplay(selectedBook) + " · " +
                   UiText.Bilingual("整本词书", "Entire book");
        }

        var sections = selectedBook.Sections
            .Where(section => section.Entries.Any(entry => ids.Contains(entry.Id)))
            .ToList();
        if (sections.Count == 1 && ids.Count == sections[0].Entries.Count &&
            sections[0].Entries.All(e => ids.Contains(e.Id)))
        {
            return ExamTopicDisplay(selectedBook.TopicId) + " · " +
                   ExamBookDisplay(selectedBook) + " · " +
                   ExamSectionDisplay(sections[0]);
        }

        return ExamTopicDisplay(selectedBook.TopicId) + " · " +
               ExamBookDisplay(selectedBook) + " · " +
               UiText.Bilingual($"{sections.Count} 个章节 · 已选 {ids.Count} 词",
                   $"{sections.Count} sections · {ids.Count} selected words");
    }
}

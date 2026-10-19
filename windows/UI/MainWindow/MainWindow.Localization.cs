using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Lexi;

public partial class MainWindow
{
    private bool _loadingLanguage;

    private void BindLanguageEvents()
    {
        LanguageToggleBtn.Click += (_, _) => SetUiLanguage(_settings.UiLanguage == "en" ? "zh-CN" : "en");
        SettingsLanguageCombo.SelectionChanged += (_, _) =>
        {
            if (!_loadingLanguage && SettingsLanguageCombo.SelectedIndex >= 0)
                SetUiLanguage(SettingsLanguageCombo.SelectedIndex == 1 ? "en" : "zh-CN");
        };
    }

    private void LoadLanguage()
    {
        _loadingLanguage = true;
        try
        {
            SettingsLanguageCombo.SelectedIndex = _settings.UiLanguage == "en" ? 1 : 0;
            ApplyUiLanguage();
        }
        finally { _loadingLanguage = false; }
    }

    public void SetUiLanguage(string language)
    {
        var next = UiText.Normalize(language);
        if (_settings.UiLanguage != next)
        {
            var previous = _settings.UiLanguage;
            _settings.UiLanguage = next;
            try { _vocabService.SaveSettings(_settings); }
            catch (Exception ex)
            {
                _settings.UiLanguage = previous;
                SetStatus(UiText.Bilingual("界面语言未能保存：", "Could not save the interface language: ") + ex.Message);
                return;
            }
        }
        LoadLanguage();
    }

    private void ApplyUiLanguage()
    {
        UiText.Apply(_settings.UiLanguage);
        _settingsDrawer?.RefreshLanguage();
        RefreshIeltsLanguage();
        RefreshStatisticsLanguage();
        LanguageToggleBtn.Content = _settings.UiLanguage == "en" ? "ZH" : "EN";
        Title = UiText.Redisplay(Title);
        var controls = new HashSet<Control>();
        var flyouts = new HashSet<FlyoutBase>();
        RefreshUiLanguageFor(this, controls, flyouts);
        foreach (var root in _settingsRoots)
            RefreshUiLanguageFor(root, controls, flyouts);
        foreach (var flyout in _openStudyFlyouts.ToArray())
            RefreshUiLanguageForFlyout(flyout, controls, flyouts);

        _quickCard?.RefreshLanguage();
        _archiveActions?.RefreshLanguage();
        _spellingPractice?.RefreshLanguage();
        if (_spellingPractice != null && _learningTypingSession?.Current != null)
            UpdateSpellingSurface();
        RefreshDynamicLanguageSurfaces();
        UpdateQuickShortcutStatus();
        RefreshStudyShortcutHints();
        RefreshGlobalStudySurface();
    }

    private void RefreshDynamicLanguageSurfaces()
    {
        if (_refreshOpenPlanCreatorLanguage != null)
        {
            _planCreatorObservedLanguage = UiText.Language;
            _refreshOpenPlanCreatorLanguage();
        }

        if (_learningHubPage.IsVisible)
            RenderLearningPlans();

        if (_learningHubPage.IsVisible &&
            _dailyLearningSession != null &&
            _learningTypingSession == null &&
            _studyWorkspaceHost.IsVisible)
        {
            RenderDailyLearning();
        }

        if (_currentPage == "review" && _reviewWord != null)
        {
            var revealed = _reviewRevealed;
            var ratedPending = _reviewRatedPending;
            RenderReviewCard();
            if (ratedPending)
                ShowReviewRatedState();
            else if (revealed)
                RevealReview();
        }

        if (_examTopicsPage?.IsVisible == true)
            RefreshExamTopicsPage();

        RefreshSocialLanguage();
    }

    private void RefreshUiLanguageForFlyout(FlyoutBase flyout)
        => RefreshUiLanguageForFlyout(flyout, new HashSet<Control>(), new HashSet<FlyoutBase>());

    private void RefreshUiLanguageForFlyout(
        FlyoutBase flyout,
        HashSet<Control> controls,
        HashSet<FlyoutBase> flyouts)
    {
        if (!flyouts.Add(flyout)) return;
        if (flyout is Flyout regular && regular.Content is Control content)
            RefreshUiLanguageFor(content, controls, flyouts);
        if (flyout is MenuFlyout menu)
            RefreshUiLanguageItems(menu.Items, controls, flyouts);
    }

    private void RefreshUiLanguageFor(
        Control root,
        HashSet<Control> controls,
        HashSet<FlyoutBase> flyouts)
    {
        var subtree = new[] { root }
            .Concat(root.GetLogicalDescendants().OfType<Control>())
            .Concat(root.GetVisualDescendants().OfType<Control>())
            .Distinct();
        foreach (var control in subtree)
            RefreshUiLanguageControl(control, controls, flyouts);
    }

    private void RefreshUiLanguageControl(
        Control control,
        HashSet<Control> controls,
        HashSet<FlyoutBase> flyouts)
    {
        if (!controls.Add(control)) return;

        {
            switch (control)
            {
                case SelectableTextBlock:
                    // Vocabulary, quotes, examples and writing prompts are user content.
                    break;
                case TextBlock label when !string.IsNullOrEmpty(label.Text):
                    label.Text = UiText.RedisplayFor(label, label.Text);
                    break;
                case ContentControl contentControl when contentControl.Content is string caption:
                    contentControl.Content = UiText.RedisplayFor(control, caption, "content");
                    break;
                case TextBox input when !string.IsNullOrEmpty(input.Watermark):
                    input.Watermark = UiText.RedisplayFor(input, input.Watermark, "watermark");
                    break;
            }
            if (control is MenuItem menuItem && menuItem.Header is string header)
            {
                menuItem.Header = UiText.RedisplayFor(menuItem, header, "header");
                RefreshUiLanguageItems(menuItem.Items, controls, flyouts);
            }
            if (control is HeaderedContentControl headered && headered.Header is string headeredCaption)
                headered.Header = UiText.RedisplayFor(headered, headeredCaption, "header");
            if (control is ComboBox combo)
                RefreshUiLanguageItems(combo.Items, controls, flyouts);
            if (ToolTip.GetTip(control) is string tip)
                ToolTip.SetTip(control, UiText.RedisplayFor(control, tip, "tooltip"));
            var automationName = AutomationProperties.GetName(control);
            if (!string.IsNullOrEmpty(automationName))
                AutomationProperties.SetName(control, UiText.RedisplayFor(control, automationName, "automation"));
            if (control.ContextFlyout is { } contextFlyout)
                RefreshUiLanguageForFlyout(contextFlyout, controls, flyouts);
            if (control is Button { Flyout: { } buttonFlyout })
                RefreshUiLanguageForFlyout(buttonFlyout, controls, flyouts);
        }
    }

    private void RefreshUiLanguageItems(
        System.Collections.IEnumerable items,
        HashSet<Control> controls,
        HashSet<FlyoutBase> flyouts)
    {
        foreach (var item in items)
        {
            if (item is Control control)
                RefreshUiLanguageControl(control, controls, flyouts);
        }
    }
}

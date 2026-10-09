using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lexi.Features.Learning;

/// <summary>
/// 1.2.2 拼写训练设置组件：
/// - 范围区（全部词条 / 仅薄弱项），空 weak 严格禁用薄弱项
/// - 模式区（无提示默写 / 辅助拼写），清晰说明两模式区别
/// - 唯一开始键，全部文本使用 UiText.Bilingual 双语国际化
/// - 回调参数为 (onlyWeak, hints)，大字号与窄窗自适应换行不裁切
/// </summary>
public class PracticeSetupControl : UserControl
{
    private readonly Action<bool, bool> _start;
    private readonly Action _cancel;

    private readonly TextBlock _titleBlock;
    private readonly TextBlock _subtitleBlock;

    private readonly TextBlock _scopeSectionHeader;
    private readonly RadioButton _allScopeRadio;
    private readonly TextBlock _allScopeDesc;
    private readonly RadioButton _weakScopeRadio;
    private readonly TextBlock _weakScopeDesc;
    private readonly Border _weakOptionBorder;

    private readonly TextBlock _modeSectionHeader;
    private readonly RadioButton _memoryModeRadio;
    private readonly TextBlock _memoryModeDesc;
    private readonly RadioButton _assistedModeRadio;
    private readonly TextBlock _assistedModeDesc;

    private readonly TextBlock _diffBoxTitle;
    private readonly TextBlock _diffBoxText;

    private readonly Button _cancelButton;
    private readonly Button _startButton;

    public int TotalWords { get; }
    public int WeakWords { get; }

    public bool OnlyWeak => _weakScopeRadio.IsChecked == true && WeakWords > 0;
    public bool Hints => _assistedModeRadio.IsChecked == true;

    public Button StartButton => _startButton;
    public Button CancelButton => _cancelButton;
    public RadioButton AllScopeRadio => _allScopeRadio;
    public RadioButton WeakScopeRadio => _weakScopeRadio;
    public RadioButton MemoryModeRadio => _memoryModeRadio;
    public RadioButton AssistedModeRadio => _assistedModeRadio;

    public PracticeSetupControl(int total, int weak, Action<bool, bool> start, Action cancel)
    {
        TotalWords = Math.Max(0, total);
        WeakWords = Math.Max(0, weak);
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));

        var rootCard = new Border
        {
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1)
        };
        rootCard.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBrush"));
        rootCard.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));

        var mainLayout = new StackPanel { Spacing = 20 };

        // 1. 标题区（大标题 22-24，副标题 14）
        var headerStack = new StackPanel { Spacing = 4 };
        _titleBlock = new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        _titleBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        headerStack.Children.Add(_titleBlock);

        _subtitleBlock = new TextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };
        _subtitleBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        headerStack.Children.Add(_subtitleBlock);
        mainLayout.Children.Add(headerStack);

        // 2. 范围区（范围区：全部词条 / 仅薄弱项）
        var scopeGroup = new StackPanel { Spacing = 10 };
        _scopeSectionHeader = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _scopeSectionHeader.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        scopeGroup.Children.Add(_scopeSectionHeader);

        // 全部词条选项
        var allOptionBorder = CreateOptionContainer();
        var allOptionStack = new StackPanel { Spacing = 4 };
        _allScopeRadio = new RadioButton
        {
            GroupName = "PracticeScope",
            FontSize = 15,
            FontWeight = FontWeight.Medium
        };
        allOptionStack.Children.Add(_allScopeRadio);
        _allScopeDesc = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(26, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _allScopeDesc.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        allOptionStack.Children.Add(_allScopeDesc);
        allOptionBorder.Child = allOptionStack;
        scopeGroup.Children.Add(allOptionBorder);

        // 仅薄弱项选项（空 weak 严格禁用）
        _weakOptionBorder = CreateOptionContainer();
        var weakOptionStack = new StackPanel { Spacing = 4 };
        _weakScopeRadio = new RadioButton
        {
            GroupName = "PracticeScope",
            FontSize = 15,
            FontWeight = FontWeight.Medium
        };
        weakOptionStack.Children.Add(_weakScopeRadio);
        _weakScopeDesc = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(26, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _weakScopeDesc.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        weakOptionStack.Children.Add(_weakScopeDesc);
        _weakOptionBorder.Child = weakOptionStack;

        // 空 weak 判定
        if (WeakWords <= 0)
        {
            _weakScopeRadio.IsEnabled = false;
            _weakOptionBorder.Opacity = 0.55;
            _allScopeRadio.IsChecked = true;
        }
        else
        {
            _weakScopeRadio.IsEnabled = true;
            _weakScopeRadio.IsChecked = true;
        }

        scopeGroup.Children.Add(_weakOptionBorder);
        mainLayout.Children.Add(scopeGroup);

        // 3. 模式区（模式区：无提示默写 / 辅助拼写）
        var modeGroup = new StackPanel { Spacing = 10 };
        _modeSectionHeader = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _modeSectionHeader.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        modeGroup.Children.Add(_modeSectionHeader);

        // 默写选项
        var memoryOptionBorder = CreateOptionContainer();
        var memoryOptionStack = new StackPanel { Spacing = 4 };
        _memoryModeRadio = new RadioButton
        {
            GroupName = "PracticeMode",
            FontSize = 15,
            FontWeight = FontWeight.Medium,
            IsChecked = true
        };
        memoryOptionStack.Children.Add(_memoryModeRadio);
        _memoryModeDesc = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(26, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _memoryModeDesc.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        memoryOptionStack.Children.Add(_memoryModeDesc);
        memoryOptionBorder.Child = memoryOptionStack;
        modeGroup.Children.Add(memoryOptionBorder);

        // 辅助拼写选项
        var assistedOptionBorder = CreateOptionContainer();
        var assistedOptionStack = new StackPanel { Spacing = 4 };
        _assistedModeRadio = new RadioButton
        {
            GroupName = "PracticeMode",
            FontSize = 15,
            FontWeight = FontWeight.Medium
        };
        assistedOptionStack.Children.Add(_assistedModeRadio);
        _assistedModeDesc = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(26, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _assistedModeDesc.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        assistedOptionStack.Children.Add(_assistedModeDesc);
        assistedOptionBorder.Child = assistedOptionStack;
        modeGroup.Children.Add(assistedOptionBorder);

        mainLayout.Children.Add(modeGroup);

        // 4. 模式区别清晰说明框（默写 / 辅助拼写说明区别）
        var diffCard = new Border
        {
            Padding = new Thickness(14, 12),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };
        diffCard.Bind(Border.BackgroundProperty, this.GetResourceObservable("TintBrush"));
        diffCard.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));

        var diffStack = new StackPanel { Spacing = 4 };
        _diffBoxTitle = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _diffBoxTitle.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        diffStack.Children.Add(_diffBoxTitle);

        _diffBoxText = new TextBlock
        {
            FontSize = 12,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap
        };
        _diffBoxText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        diffStack.Children.Add(_diffBoxText);
        diffCard.Child = diffStack;

        mainLayout.Children.Add(diffCard);

        // 5. 底部操作区（唯一开始键 + 取消键）
        var actionsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 4, 0, 0)
        };

        _cancelButton = new Button
        {
            Classes = { "ghost" },
            Padding = new Thickness(18, 10),
            FontSize = 14
        };
        _cancelButton.Click += (_, _) => _cancel();
        Grid.SetColumn(_cancelButton, 1);
        actionsGrid.Children.Add(_cancelButton);

        _startButton = new Button
        {
            Classes = { "primary" },
            Padding = new Thickness(24, 10),
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        _startButton.Click += (_, _) => OnStartClicked();

        // 若总词数为 0 则禁用开始
        if (TotalWords <= 0)
        {
            _startButton.IsEnabled = false;
        }

        Grid.SetColumn(_startButton, 2);
        actionsGrid.Children.Add(_startButton);

        mainLayout.Children.Add(actionsGrid);

        rootCard.Child = mainLayout;
        Content = rootCard;

        RefreshLanguage();
    }

    private void OnStartClicked()
    {
        var targetOnlyWeak = OnlyWeak;
        var targetHints = Hints;
        _start(targetOnlyWeak, targetHints);
    }

    private Border CreateOptionContainer()
    {
        var border = new Border
        {
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1)
        };
        border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        return border;
    }

    /// <summary>
    /// 双语刷新所有 UI 文本（所有 UiText.Bilingual）。
    /// </summary>
    public void RefreshLanguage()
    {
        _titleBlock.Text = UiText.Bilingual("拼写训练准备", "Practice Setup");
        _subtitleBlock.Text = UiText.Bilingual(
            "选择训练范围与模式，针对性强化单词拼写记忆。",
            "Configure practice scope and mode to target spelling retention.");

        _scopeSectionHeader.Text = UiText.Bilingual("1. 练习范围", "1. Practice Scope");
        _allScopeRadio.Content = UiText.Bilingual($"全部词条（共 {TotalWords} 词）", $"All Words ({TotalWords} words)");
        _allScopeDesc.Text = UiText.Bilingual(
            "遍历当前计划或词库中的全部词汇，进行完整拼写检验。",
            "Iterate through all vocabulary items in the plan for a complete spelling check.");

        var weakText = WeakWords > 0
            ? UiText.Bilingual($"仅薄弱项（共 {WeakWords} 词）", $"Weak Words Only ({WeakWords} words)")
            : UiText.Bilingual("仅薄弱项（暂无薄弱词）", "Weak Words Only (No weak words)");
        _weakScopeRadio.Content = weakText;

        _weakScopeDesc.Text = WeakWords > 0
            ? UiText.Bilingual("聚焦近期评测中判定为「忘记」或拼写出错的词汇，专项攻克突破。", "Focus specifically on words recently rated 'Forgot' or misspelled.")
            : UiText.Bilingual("当前没有错误或薄弱词汇记录，可直接选择「全部词条」开启练习。", "No weak or mistaken words currently recorded. Select 'All Words' to begin.");

        _modeSectionHeader.Text = UiText.Bilingual("2. 训练模式", "2. Practice Mode");
        _memoryModeRadio.Content = UiText.Bilingual("无提示默写（主动回忆）", "Spelling from Memory (Active Recall)");
        _memoryModeDesc.Text = UiText.Bilingual(
            "完全隐藏拼写，仅凭释义和发音独立默写出单词。无任何字母线索，考核真实记忆深度。",
            "Hides word spelling entirely; recall purely from meaning and sound with zero letter cues.");

        _assistedModeRadio.Content = UiText.Bilingual("辅助拼写（渐进提示）", "Assisted Spelling (Gradual Hints)");
        _assistedModeDesc.Text = UiText.Bilingual(
            "输入过程中提供单词字长、首字母或淡色辅助线索，降低记忆负荷，适合巩固新学词汇。",
            "Provides word length, first letter, or subtle cues during input, ideal for newly learned words.");

        _diffBoxTitle.Text = UiText.Bilingual("【默写 vs 辅助拼写 区别说明】", "[Mode Difference: Memory vs Assisted]");
        _diffBoxText.Text = UiText.Bilingual(
            "• 默写：不提供任何字母线索，考察 100% 自主主动回忆，强化长效神经回路，适合考前冲刺与自测。\n• 辅助拼写：提供字长占位与渐进式线索，降低输入受挫感，适合生词巩固与建立拼写手感。",
            "• Spelling from Memory: Zero letter hints, testing 100% active recall to solidify long-term memory; best for testing.\n• Assisted Spelling: Letter placeholders and gradual cues reduce typing frustration; best for reinforcement.");

        _cancelButton.Content = UiText.Bilingual("取消", "Cancel");
        _startButton.Content = UiText.Bilingual("开始训练", "Start Practice");
    }
}

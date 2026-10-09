using System;
using Lexi.Features.Ielts;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;
using UiText = Lexi.Features.Ielts.UiText;

namespace Lexi.Controls;

/// <summary>
/// 档案式词汇行组件：
/// 包含选择框、英文词头、音标、词性、简短释义、学习状态、紧凑发音动作；
/// 展开后呈现抽屉式详情（完整释义、例句与朗读、教材来源、补充说明、同义替换、收藏/查看档案）。
/// 遵循契约：选择、展开、发音、收藏操作相互独立，资源缺字段时不强行占位。
/// </summary>
public class VocabularyRow : Border
{
    private readonly CheckBox _checkBox;
    private readonly ToggleButton _chevron;
    private readonly TextBlock _wordBlock;
    private readonly TextBlock _phoneticBlock;
    private readonly Border _posBorder;
    private readonly TextBlock _posBlock;
    private readonly TextBlock _compactMeaning;
    private readonly Border _statusBorder;
    private readonly TextBlock _statusBlock;
    private readonly Button _audioBtn;

    // 抽屉详情
    private readonly Border _drawer;
    private readonly TextBlock _meaningTitle;
    private readonly TextBlock _fullMeaningBlock;
    private readonly StackPanel _examplePanel;
    private readonly TextBlock _exampleTitle;
    private readonly TextBlock _exampleBlock;
    private readonly Button _exampleAudioBtn;
    private readonly StackPanel _sourcePanel;
    private readonly TextBlock _sourceTitle;
    private readonly TextBlock _sourceBlock;
    private readonly StackPanel _extraPanel;
    private readonly TextBlock _extraTitle;
    private readonly TextBlock _extraBlock;
    private readonly StackPanel _synonymsPanel;
    private readonly TextBlock _synonymsTitle;
    private readonly WrapPanel _synonymsWrap;
    private readonly Button _archiveBtn;

    public string WordId { get; private set; } = "";
    public string WordText { get; private set; } = "";
    public string Phonetic { get; private set; } = "";
    public string Pos { get; private set; } = "";
    public string Meaning { get; private set; } = "";
    public string Example { get; private set; } = "";
    public string Extra { get; private set; } = "";
    public string SourceTitle { get; private set; } = "";
    public IReadOnlyList<string> Synonyms { get; private set; } = Array.Empty<string>();
    public string AudioPath { get; private set; } = "";
    public string StatusKind { get; private set; } = "new";
    public bool IsArchived { get; private set; }

    public bool IsSelected => _checkBox.IsChecked == true;
    public bool IsExpanded => _chevron.IsChecked == true;

    public event Action<VocabularyRow, bool>? SelectionToggled;
    public event Action<VocabularyRow, bool>? ExpansionToggled;
    public event Action<VocabularyRow, string>? PlayAudioRequested;
    public event Action<VocabularyRow, string>? PlayExampleRequested;
    public event Action<VocabularyRow>? AddToArchiveRequested;
    public event Action<VocabularyRow, string>? ViewArchiveRequested;

    public VocabularyRow()
    {
        // 根边框样式（中性下边框）
        Classes.Add("vocabulary-row");
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(14, 10);
        Background = Brushes.Transparent;

        // 主容器（垂直：主行 + 展开抽屉）
        var rootPanel = new StackPanel { Spacing = 4 };

        // 主行 Grid：选择框、折叠箭头、词头/音标/释义、状态徽章、发音按钮
        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        // 1. 选择框（只管理批量成员，不触发展开或发音）
        _checkBox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _checkBox.IsCheckedChanged += (_, _) => SelectionToggled?.Invoke(this, IsSelected);
        Grid.SetColumn(_checkBox, 0);
        mainGrid.Children.Add(_checkBox);

        // 2. 独立展开折叠切换按钮（只改变展开状态，不改选择或发音）
        _chevron = new ToggleButton
        {
            Classes = { "chevron-toggle" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _chevron.IsCheckedChanged += (_, _) =>
        {
            var exp = IsExpanded;
            if (_drawer != null) _drawer.IsVisible = exp;
            if (_compactMeaning != null) _compactMeaning.IsVisible = !exp;
            ExpansionToggled?.Invoke(this, exp);
        };
        Grid.SetColumn(_chevron, 1);
        mainGrid.Children.Add(_chevron);

        // 3. 单词信息区（词头、音标、词性、简短释义）
        var wordStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerWrap = new WrapPanel { Orientation = Orientation.Horizontal };
        _wordBlock = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        try { _wordBlock.Cursor = new Cursor(StandardCursorType.Hand); } catch { }
        // 点击词头同样触发展开/收起，便于触控与鼠标操作
        _wordBlock.PointerPressed += (_, _) => _chevron.IsChecked = !_chevron.IsChecked;

        _phoneticBlock = new TextBlock
        {
            Classes = { "muted" },
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        _posBlock = new TextBlock { FontSize = 11 };
        _posBorder = new Border
        {
            Classes = { "pos-chip" },
            VerticalAlignment = VerticalAlignment.Center,
            Child = _posBlock
        };

        headerWrap.Children.Add(_wordBlock);
        headerWrap.Children.Add(_phoneticBlock);
        headerWrap.Children.Add(_posBorder);
        wordStack.Children.Add(headerWrap);

        // 收起时的紧凑释义（单行省略）
        _compactMeaning = new TextBlock
        {
            Classes = { "muted" },
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        wordStack.Children.Add(_compactMeaning);

        Grid.SetColumn(wordStack, 2);
        mainGrid.Children.Add(wordStack);

        // 4. 状态徽章
        _statusBlock = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.SemiBold
        };
        _statusBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 0),
            Child = _statusBlock
        };
        Grid.SetColumn(_statusBorder, 3);
        mainGrid.Children.Add(_statusBorder);

        // 5. 紧凑发音动作（只播放，不改选择或展开）
        var speakerPath = new Path
        {
            Data = SafeParseGeometry("M 3,6 L 6,6 L 10,2 L 10,14 L 6,10 L 3,10 Z M 12,5.5 C 13.5,7 13.5,9 12,10.5"),
            StrokeThickness = 1.2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 14,
            Height = 14
        };
        speakerPath.Bind(Shape.StrokeProperty, this.GetResourceObservable("MutedBrush"));

        _audioBtn = new Button
        {
            Classes = { "row-btn" },
            Padding = new Thickness(6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Content = speakerPath
        };
        _audioBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(AudioPath))
                PlayAudioRequested?.Invoke(this, AudioPath);
            else if (!string.IsNullOrEmpty(WordText))
                PlayAudioRequested?.Invoke(this, WordText);
        };
        Grid.SetColumn(_audioBtn, 4);
        mainGrid.Children.Add(_audioBtn);

        rootPanel.Children.Add(mainGrid);

        // 展开抽屉（含完整释义、例句、来源、补充、同义替换）
        _drawer = new Border
        {
            Classes = { "drawer-slot" },
            Padding = new Thickness(14, 10),
            Margin = new Thickness(36, 6, 0, 4),
            IsVisible = false
        };

        var drawerStack = new StackPanel { Spacing = 8 };

        // 完整释义
        var meaningPanel = new StackPanel { Spacing = 2 };
        _meaningTitle = new TextBlock { Classes = { "eyebrow" }, FontSize = 10, Text = "完整释义" };
        _fullMeaningBlock = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
        meaningPanel.Children.Add(_meaningTitle);
        meaningPanel.Children.Add(_fullMeaningBlock);
        drawerStack.Children.Add(meaningPanel);

        // 例句（缺字段时不造数据、不强行留空占位）
        _examplePanel = new StackPanel { Spacing = 2 };
        _exampleTitle = new TextBlock { Classes = { "eyebrow" }, FontSize = 10, Text = "例句" };
        var exampleGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _exampleBlock = new TextBlock { Classes = { "example-en" }, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        Grid.SetColumn(_exampleBlock, 0);
        exampleGrid.Children.Add(_exampleBlock);

        var exampleSpeaker = new Path
        {
            Data = SafeParseGeometry("M 3,6 L 6,6 L 10,2 L 10,14 L 6,10 L 3,10 Z M 12,5.5 C 13.5,7 13.5,9 12,10.5"),
            StrokeThickness = 1.2,
            StrokeLineCap = PenLineCap.Round,
            Width = 14,
            Height = 14
        };
        exampleSpeaker.Bind(Shape.StrokeProperty, this.GetResourceObservable("MutedBrush"));

        _exampleAudioBtn = new Button
        {
            Classes = { "row-btn" },
            Padding = new Thickness(4, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Content = exampleSpeaker
        };
        _exampleAudioBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(Example))
                PlayExampleRequested?.Invoke(this, Example);
        };
        Grid.SetColumn(_exampleAudioBtn, 1);
        exampleGrid.Children.Add(_exampleAudioBtn);

        _examplePanel.Children.Add(_exampleTitle);
        _examplePanel.Children.Add(exampleGrid);
        drawerStack.Children.Add(_examplePanel);

        // 教材来源
        _sourcePanel = new StackPanel { Spacing = 2 };
        _sourceTitle = new TextBlock { Classes = { "eyebrow" }, FontSize = 10, Text = "教材来源" };
        _sourceBlock = new TextBlock { Classes = { "muted" }, FontSize = 12 };
        _sourcePanel.Children.Add(_sourceTitle);
        _sourcePanel.Children.Add(_sourceBlock);
        drawerStack.Children.Add(_sourcePanel);

        // 补充说明
        _extraPanel = new StackPanel { Spacing = 2 };
        _extraTitle = new TextBlock { Classes = { "eyebrow" }, FontSize = 10, Text = "补充说明" };
        _extraBlock = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        _extraPanel.Children.Add(_extraTitle);
        _extraPanel.Children.Add(_extraBlock);
        drawerStack.Children.Add(_extraPanel);

        // 同义替换
        _synonymsPanel = new StackPanel { Spacing = 4 };
        _synonymsTitle = new TextBlock { Classes = { "eyebrow" }, FontSize = 10, Text = "同义替换" };
        _synonymsWrap = new WrapPanel { Orientation = Orientation.Horizontal };
        _synonymsPanel.Children.Add(_synonymsTitle);
        _synonymsPanel.Children.Add(_synonymsWrap);
        drawerStack.Children.Add(_synonymsPanel);

        // 档案收藏/查看动作
        _archiveBtn = new Button
        {
            Classes = { "secondary" },
            Padding = new Thickness(10, 5),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _archiveBtn.Click += (_, _) =>
        {
            if (IsArchived)
                ViewArchiveRequested?.Invoke(this, WordText);
            else
                AddToArchiveRequested?.Invoke(this);
        };
        drawerStack.Children.Add(_archiveBtn);

        _drawer.Child = drawerStack;
        rootPanel.Children.Add(_drawer);

        Child = rootPanel;

        // 绑定主题动态颜色
        this.Bind(BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        _wordBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        _statusBorder.Bind(BackgroundProperty, this.GetResourceObservable("TintBrush"));

        RefreshLanguage();
    }

    /// <summary>
    /// 填充或更新词行数据
    /// </summary>
    public void BindWord(
        string id,
        string word,
        string phonetic,
        string pos,
        string meaning,
        string example,
        string extra,
        string sourceTitle,
        IReadOnlyList<string> synonyms,
        string audioPath,
        string statusKind,
        bool isSelected,
        bool isExpanded = false,
        bool isArchived = false)
    {
        WordId = id;
        WordText = word;
        Phonetic = phonetic;
        Pos = pos;
        Meaning = meaning;
        Example = example;
        Extra = extra;
        SourceTitle = sourceTitle;
        Synonyms = synonyms ?? Array.Empty<string>();
        AudioPath = audioPath;
        StatusKind = statusKind;
        IsArchived = isArchived;

        _checkBox.IsChecked = isSelected;
        _chevron.IsChecked = isExpanded;
        _drawer.IsVisible = isExpanded;
        _compactMeaning.IsVisible = !isExpanded;

        _wordBlock.Text = word;
        _phoneticBlock.Text = string.IsNullOrWhiteSpace(phonetic) ? "" : phonetic;
        _phoneticBlock.IsVisible = !string.IsNullOrWhiteSpace(phonetic);

        _posBlock.Text = pos;
        _posBorder.IsVisible = !string.IsNullOrWhiteSpace(pos);

        _compactMeaning.Text = meaning;
        _fullMeaningBlock.Text = meaning;

        // 例句（缺省不显示）
        var hasExample = !string.IsNullOrWhiteSpace(example);
        _examplePanel.IsVisible = hasExample;
        _exampleBlock.Text = example ?? "";

        // 教材来源（缺省不显示）
        var hasSource = !string.IsNullOrWhiteSpace(sourceTitle);
        _sourcePanel.IsVisible = hasSource;
        _sourceBlock.Text = sourceTitle ?? "";

        // 补充说明（缺省不显示）
        var hasExtra = !string.IsNullOrWhiteSpace(extra);
        _extraPanel.IsVisible = hasExtra;
        _extraBlock.Text = extra ?? "";

        // 同义替换（缺省不显示）
        var hasSynonyms = Synonyms.Count > 0;
        _synonymsPanel.IsVisible = hasSynonyms;
        _synonymsWrap.Children.Clear();
        if (hasSynonyms)
        {
            foreach (var syn in Synonyms)
            {
                var chip = new Border
                {
                    Classes = { "pos-chip" },
                    Margin = new Thickness(0, 0, 6, 4),
                    Child = new TextBlock { Text = syn, FontSize = 12 }
                };
                _synonymsWrap.Children.Add(chip);
            }
        }

        UpdateStatusVisual();
        UpdateArchiveVisual();
        RefreshLanguage();
    }

    public void SetSelected(bool selected)
    {
        if (_checkBox.IsChecked != selected)
            _checkBox.IsChecked = selected;
    }

    public void SetExpanded(bool expanded)
    {
        if (_chevron.IsChecked != expanded)
            _chevron.IsChecked = expanded;
    }

    public void SetArchived(bool isArchived)
    {
        IsArchived = isArchived;
        UpdateArchiveVisual();
    }

    private void UpdateArchiveVisual()
    {
        _archiveBtn.Content = IsArchived ? IeltsI18n.T("查看档案") : IeltsI18n.T("加入词汇档案");
        _archiveBtn.SetValue(ToolTip.TipProperty, IsArchived
            ? IeltsI18n.T("已在档案中 · 查看档案")
            : IeltsI18n.T("加入词汇档案"));
    }

    private void UpdateStatusVisual()
    {
        var isEn = UiText.Language == "en";
        switch (StatusKind)
        {
            case "typed":
                _statusBlock.Text = isEn ? "Practiced" : "已练习";
                _statusBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PrimaryGreen"));
                break;
            case "error":
                _statusBlock.Text = isEn ? "Error" : "错误词";
                _statusBlock.Foreground = Brushes.IndianRed;
                break;
            default:
                _statusBlock.Text = isEn ? "New" : "未练习";
                _statusBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
                break;
        }
    }

    public void RefreshLanguage()
    {
        var isEn = UiText.Language == "en";
        _meaningTitle.Text = isEn ? "Full Meaning" : "完整释义";
        _exampleTitle.Text = isEn ? "Example" : "例句";
        _sourceTitle.Text = isEn ? "Source" : "教材来源";
        _extraTitle.Text = isEn ? "Notes" : "补充说明";
        _synonymsTitle.Text = isEn ? "Synonyms" : "同义替换";

        _checkBox.SetValue(ToolTip.TipProperty, isEn ? "Select word" : "选择词汇");
        _chevron.SetValue(ToolTip.TipProperty, isEn ? "Expand/collapse details" : "展开/收起详情");
        _audioBtn.SetValue(ToolTip.TipProperty, isEn ? "Pronounce" : "朗读发音");
        _exampleAudioBtn.SetValue(ToolTip.TipProperty, isEn ? "Read example" : "朗读例句");

        UpdateStatusVisual();
        UpdateArchiveVisual();
    }

    private static Geometry? SafeParseGeometry(string data)
    {
        try { return Geometry.Parse(data); }
        catch { return null; }
    }
}

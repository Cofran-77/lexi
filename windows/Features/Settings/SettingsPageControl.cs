using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lexi.Features.Settings;

/// <summary>
/// 1.2.2 全页面手风琴设置组件：
/// - 完整页面呈现，最大宽度 960 DIP，内边距 24 DIP，单 ScrollViewer 滚动
/// - 手风琴 5 大分类（外观、学习与快捷键、AI 服务、数据与备份、关于），一次仅展开一类
/// - 标题字号 24，正文默认 14-16，文本自适应换行，窄窗及大字号不裁切
/// - 兼容原有 SettingsDrawerControl 全部公开 API 与事件，支持现有设置控件迁入
/// </summary>
public class SettingsPageControl : UserControl
{
    private sealed class SectionCardSlot
    {
        public SettingsSection Section { get; init; }
        public Border Container { get; init; } = null!;
        public Button HeaderButton { get; init; } = null!;
        public TextBlock IconBlock { get; init; } = null!;
        public TextBlock TitleBlock { get; init; } = null!;
        public TextBlock SummaryBlock { get; init; } = null!;
        public TextBlock ArrowBlock { get; init; } = null!;
        public Border ContentSectionBorder { get; init; } = null!;
        public ContentControl ContentHost { get; init; } = null!;
        public Border FooterBar { get; init; } = null!;
        public ContentControl FooterHost { get; init; } = null!;
    }

    private readonly TextBlock _headerEyebrowBlock;
    private readonly TextBlock _headerTitleBlock;
    private readonly TextBlock _headerSubtitleBlock;
    private readonly TextBlock _statusMessageBlock;
    private readonly Button _closeButton;

    private readonly StackPanel _accordionStack;
    private readonly Dictionary<SettingsSection, SectionCardSlot> _slots = new();
    private readonly Dictionary<SettingsSection, Control> _sectionContents = new();
    private readonly Dictionary<SettingsSection, Control?> _sectionFooters = new();

    public ScrollViewer BodyScroller { get; }

    public SettingsSection CurrentSection { get; private set; } = SettingsSection.None;
    public bool IsOpen => IsVisible;

    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public event EventHandler<SettingsSection>? SectionChanged;

    public SettingsPageControl()
    {
        IsVisible = false;

        // 顶层单滚动容器
        BodyScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        // 页面核心容器：居中、最大宽度 960 DIP、内边距 24 DIP
        var pageContainer = new StackPanel
        {
            MaxWidth = 960,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(24),
            Spacing = 20
        };

        // 1. 顶部 Header 区域（标题 24，状态提示，关闭按键）
        var headerCard = new Border
        {
            Padding = new Thickness(20, 16),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        headerCard.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBrush"));
        headerCard.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };

        var titleStack = new StackPanel { Spacing = 4 };

        _headerEyebrowBlock = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            TextWrapping = TextWrapping.Wrap
        };
        _headerEyebrowBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        titleStack.Children.Add(_headerEyebrowBlock);

        _headerTitleBlock = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        _headerTitleBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        titleStack.Children.Add(_headerTitleBlock);

        _headerSubtitleBlock = new TextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };
        _headerSubtitleBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        titleStack.Children.Add(_headerSubtitleBlock);

        Grid.SetColumn(titleStack, 0);
        headerGrid.Children.Add(titleStack);

        _statusMessageBlock = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        Grid.SetColumn(_statusMessageBlock, 1);
        headerGrid.Children.Add(_statusMessageBlock);

        _closeButton = new Button
        {
            Content = "✕",
            Classes = { "ghost" },
            Padding = new Thickness(12, 8),
            VerticalAlignment = VerticalAlignment.Top
        };
        _closeButton.Click += (_, _) => Close();
        Grid.SetColumn(_closeButton, 2);
        headerGrid.Children.Add(_closeButton);

        headerCard.Child = headerGrid;
        pageContainer.Children.Add(headerCard);

        // 2. 手风琴 5 大分类列表（一次展开一类）
        _accordionStack = new StackPanel { Spacing = 14 };

        CreateAccordionSlot(SettingsSection.Appearance, "🎨");
        CreateAccordionSlot(SettingsSection.StudyAndShortcuts, "⌨️");
        CreateAccordionSlot(SettingsSection.AiService, "✨");
        CreateAccordionSlot(SettingsSection.DataAndBackup, "💾");
        CreateAccordionSlot(SettingsSection.About, "ℹ️");

        pageContainer.Children.Add(_accordionStack);

        BodyScroller.Content = pageContainer;
        Content = BodyScroller;

        RefreshLanguage();
    }

    private void CreateAccordionSlot(SettingsSection section, string icon)
    {
        var slotBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        slotBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("CardBrush"));
        slotBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));

        var slotLayout = new StackPanel();

        // 卡片 Header 按钮（自适应窄窗与大字号）
        var headerBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(20, 16)
        };
        headerBtn.Classes.Add("ghost");
        headerBtn.Classes.Add("settings-section-card");

        var btnGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };
        btnGrid.Children.Add(iconBlock);

        var textStack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleBlock = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        titleBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        textStack.Children.Add(titleBlock);

        var summaryBlock = new TextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };
        summaryBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        textStack.Children.Add(summaryBlock);

        Grid.SetColumn(textStack, 1);
        btnGrid.Children.Add(textStack);

        var arrowBlock = new TextBlock
        {
            Text = "›",
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        arrowBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        Grid.SetColumn(arrowBlock, 2);
        btnGrid.Children.Add(arrowBlock);

        headerBtn.Content = btnGrid;
        headerBtn.Click += (_, _) =>
        {
            if (CurrentSection == section)
            {
                ShowCategories(); // 若已展开则收起
            }
            else
            {
                ShowSection(section); // 展开目标分类，自动折叠其余类
            }
        };
        slotLayout.Children.Add(headerBtn);

        // 展开后的内容区（手风琴展开体）
        var contentSectionBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 18),
            IsVisible = false
        };
        contentSectionBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));

        var contentLayout = new StackPanel { Spacing = 16 };

        var contentHost = new ContentControl();
        contentLayout.Children.Add(contentHost);

        var footerBar = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
            IsVisible = false
        };
        footerBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("LineBrush"));
        var footerHost = new ContentControl();
        footerBar.Child = footerHost;
        contentLayout.Children.Add(footerBar);

        contentSectionBorder.Child = contentLayout;
        slotLayout.Children.Add(contentSectionBorder);

        slotBorder.Child = slotLayout;
        _accordionStack.Children.Add(slotBorder);

        _slots[section] = new SectionCardSlot
        {
            Section = section,
            Container = slotBorder,
            HeaderButton = headerBtn,
            IconBlock = iconBlock,
            TitleBlock = titleBlock,
            SummaryBlock = summaryBlock,
            ArrowBlock = arrowBlock,
            ContentSectionBorder = contentSectionBorder,
            ContentHost = contentHost,
            FooterBar = footerBar,
            FooterHost = footerHost
        };
    }

    /// <summary>
    /// 打开全页面设置。默认展开外观分类。
    /// </summary>
    public void Open(SettingsSection section = SettingsSection.Appearance)
    {
        IsVisible = true;
        if (section != SettingsSection.None)
        {
            ShowSection(section);
        }
        else
        {
            ShowCategories();
        }
        Opened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 关闭设置页面。
    /// </summary>
    public void Close()
    {
        if (!IsVisible) return;
        IsVisible = false;
        CurrentSection = SettingsSection.None;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 注册某一分类的具体内容控件（由主控将现有设置控件迁入）。
    /// </summary>
    public void RegisterSectionContent(SettingsSection section, Control content, Control? footerActions = null)
    {
        _sectionContents[section] = content;
        _sectionFooters[section] = footerActions;

        if (_slots.TryGetValue(section, out var slot))
        {
            slot.ContentHost.Content = content;
            if (footerActions != null)
            {
                slot.FooterHost.Content = footerActions;
                slot.FooterBar.IsVisible = true;
            }
            else
            {
                slot.FooterHost.Content = null;
                slot.FooterBar.IsVisible = false;
            }
        }

        if (CurrentSection == section)
        {
            ShowSection(section);
        }
    }

    /// <summary>
    /// 展开指定手风琴分类，并自动折叠其余所有分类（五类一次展开一类）。
    /// </summary>
    public void ShowSection(SettingsSection section)
    {
        CurrentSection = section;

        foreach (var (sec, slot) in _slots)
        {
            if (sec == section && section != SettingsSection.None)
            {
                // 展开目标分类
                slot.ContentSectionBorder.IsVisible = true;
                slot.ArrowBlock.Text = "⌄";

                // 挂载内容
                if (_sectionContents.TryGetValue(sec, out var content))
                {
                    slot.ContentHost.Content = content;
                }
                else
                {
                    slot.ContentHost.Content = CreateEmptySectionPlaceholder(sec);
                }

                if (_sectionFooters.TryGetValue(sec, out var footer) && footer != null)
                {
                    slot.FooterHost.Content = footer;
                    slot.FooterBar.IsVisible = true;
                }
                else
                {
                    slot.FooterHost.Content = null;
                    slot.FooterBar.IsVisible = false;
                }
            }
            else
            {
                // 折叠其他所有分类
                slot.ContentSectionBorder.IsVisible = false;
                slot.ArrowBlock.Text = "›";
            }
        }

        SectionChanged?.Invoke(this, section);
    }

    /// <summary>
    /// 收起所有手风琴分类，回到全类别列表总览状态。
    /// </summary>
    public void ShowCategories()
    {
        CurrentSection = SettingsSection.None;

        foreach (var slot in _slots.Values)
        {
            slot.ContentSectionBorder.IsVisible = false;
            slot.ArrowBlock.Text = "›";
        }

        _statusMessageBlock.IsVisible = false;
        SectionChanged?.Invoke(this, SettingsSection.None);
    }

    /// <summary>
    /// 设置右上角状态提示（如“已保存”、“保存失败”）。
    /// </summary>
    public void SetStatusMessage(string message, bool isError = false)
    {
        if (string.IsNullOrEmpty(message))
        {
            _statusMessageBlock.IsVisible = false;
            return;
        }

        _statusMessageBlock.Text = message;
        _statusMessageBlock.IsVisible = true;
        if (isError)
        {
            _statusMessageBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("DangerBrush"));
        }
        else
        {
            _statusMessageBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        }
    }

    /// <summary>
    /// 全局按键处理：Esc 键优先收起当前手风琴或关闭设置。
    /// </summary>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        if (!IsVisible) return false;

        if (e.Key == Key.Escape)
        {
            if (CurrentSection != SettingsSection.None)
            {
                ShowCategories();
            }
            else
            {
                Close();
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// 刷新双语界面文本，确保中英切换即时生效。
    /// </summary>
    public void RefreshLanguage()
    {
        _headerEyebrowBlock.Text = UiText.Bilingual("系统偏好 · 保持离线", "SYSTEM PREFERENCES · OFFLINE FIRST");
        _headerTitleBlock.Text = UiText.Bilingual("设置", "Settings");
        _headerSubtitleBlock.Text = UiText.Bilingual(
            "基础释义始终离线。手风琴式分类管理外观、按键、AI与数据偏好。",
            "Offline dictionary always works. Manage appearance, shortcuts, AI, and data preferences.");

        _closeButton.Content = "✕";

        foreach (var (section, slot) in _slots)
        {
            slot.TitleBlock.Text = GetSectionTitle(section);
            slot.SummaryBlock.Text = GetSectionSummary(section);

            if (!_sectionContents.ContainsKey(section) && slot.ContentSectionBorder.IsVisible)
            {
                slot.ContentHost.Content = CreateEmptySectionPlaceholder(section);
            }
        }
    }

    private Control CreateEmptySectionPlaceholder(SettingsSection section)
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 20, 0, 20),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var note = new TextBlock
        {
            Text = $"{GetSectionTitle(section)} - {UiText.Bilingual("等待主控挂载设置项", "Waiting for host controls to be mounted")}",
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        note.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
        panel.Children.Add(note);

        return panel;
    }

    public static string GetSectionTitle(SettingsSection section) => section switch
    {
        SettingsSection.Appearance => UiText.Bilingual("外观与界面", "Appearance & Interface"),
        SettingsSection.StudyAndShortcuts => UiText.Bilingual("学习与快捷键", "Study & Shortcuts"),
        SettingsSection.AiService => UiText.Bilingual("AI 服务配置", "AI Service Settings"),
        SettingsSection.DataAndBackup => UiText.Bilingual("数据与备份", "Data & Backup"),
        SettingsSection.About => UiText.Bilingual("关于 Lexi", "About Lexi"),
        _ => UiText.Bilingual("设置", "Settings")
    };

    public static string GetSectionSummary(SettingsSection section) => section switch
    {
        SettingsSection.Appearance => UiText.Bilingual("主题模式、字号大小与界面视觉偏好", "Theme mode, font sizing, and visual preferences"),
        SettingsSection.StudyAndShortcuts => UiText.Bilingual("学习交互按键绑定、复习队列与评测行为", "Study interaction keybindings, review queue, and rating behavior"),
        SettingsSection.AiService => UiText.Bilingual("API 密钥、大模型端点与提示词配置", "API keys, LLM endpoints, and prompt configuration"),
        SettingsSection.DataAndBackup => UiText.Bilingual("词库备份、导入导出与数据维护", "Vocabulary backup, import/export, and data maintenance"),
        SettingsSection.About => UiText.Bilingual("版本信息、开源许可与致谢", "Version information, open-source licenses, and credits"),
        _ => ""
    };
}

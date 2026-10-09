using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lexi.Controls;

/// <summary>统一学习内容模型：单词、音标、释义、细节与示例，供 StudyCanvasControl 渲染。</summary>
public sealed class StudyCanvasModel
{
    public string Word { get; set; } = "";
    public string Phonetic { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Details { get; set; } = "";
    public string Example { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public bool MeaningVisible { get; set; } = true;
}

/// <summary>
/// 1.2.2 统一背词内容组件：focus / 计划 / 今日复习共享。
/// 仅渲染纯色背景上的单词正文（全部使用 SelectableTextBlock 支持选中复制），
/// 不含白色巨卡与固定宽度，随宿主宽度自适应换行；滚动由宿主负责。
/// 所有字色直接读取当前 Avalonia 主题资源（InkBrush / MutedBrush），
/// 浅 / 深主题切换时自动即时刷新，无需重开 session 或重置评分 / 揭晓。
/// 背景由宿主（focusHost / studyWorkspaceHost / 全局 Surface / 复习卡）读取 PaperBrush。
/// </summary>
public sealed class StudyCanvasControl : UserControl
{
    public StudyCanvasControl() => Render(new StudyCanvasModel());

    public void Render(StudyCanvasModel model)
    {
        var content = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };

        var word = new SelectableTextBlock
        {
            Text = model.Word,
            FontSize = 40,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        word.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
        content.Children.Add(word);

        if (!string.IsNullOrWhiteSpace(model.Phonetic))
        {
            var phonetic = new SelectableTextBlock
            {
                Text = model.Phonetic,
                FontSize = 17,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            phonetic.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
            content.Children.Add(phonetic);
        }

        if (model.MeaningVisible)
        {
            if (!string.IsNullOrWhiteSpace(model.Meaning))
            {
                var meaning = new SelectableTextBlock
                {
                    Text = model.Meaning,
                    FontSize = 22,
                    FontWeight = FontWeight.Medium,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                meaning.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InkBrush"));
                content.Children.Add(meaning);
            }

            if (!string.IsNullOrWhiteSpace(model.Details))
            {
                var details = new SelectableTextBlock
                {
                    Text = model.Details,
                    FontSize = 16,
                    LineHeight = 26,
                    Opacity = 0.9,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                details.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
                content.Children.Add(details);
            }

            if (!string.IsNullOrWhiteSpace(model.Example))
            {
                var example = new SelectableTextBlock
                {
                    Text = model.Example,
                    FontSize = 16,
                    LineHeight = 26,
                    Opacity = 0.9,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                example.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
                content.Children.Add(example);
            }
        }
        else if (!string.IsNullOrWhiteSpace(model.Placeholder))
        {
            var placeholder = new SelectableTextBlock
            {
                Text = model.Placeholder,
                FontSize = 15,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 10, 0, 0)
            };
            placeholder.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedBrush"));
            content.Children.Add(placeholder);
        }

        Content = content;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Lexi;

/// <summary>UI-only translation. Vocabulary, notes and AI content never pass through this catalog.</summary>
internal static class UiText
{
    internal static string Language { get; private set; } = "zh-CN";
    private static readonly Dictionary<string, (string Chinese, string English)> Rendered = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissingEnglish = new(StringComparer.Ordinal);
    private sealed class Caption { public string Source = ""; public string Last = ""; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Dictionary<string, Caption>> Captions = new();
    internal static IReadOnlyCollection<string> MissingEnglishKeysForAudit => MissingEnglish;
    internal static string Normalize(string? language) => language == "en" ? "en" : "zh-CN";
    internal static string Text(string chinese)
    {
        if (!UiCatalog.English.TryGetValue(chinese, out var english))
        {
            MissingEnglish.Add(chinese);
            english = chinese;
        }
        Remember(chinese, english);
        return Language == "en" ? english : chinese;
    }
    internal static string Bilingual(string chinese, string english, string? language = null)
    {
        UiCatalog.English.TryAdd(chinese, english);
        Remember(chinese, english);
        return (language ?? Language) == "en" ? english : chinese;
    }
    internal static string Format(FormattableString value)
    {
        var chinese = value.ToString(CultureInfo.CurrentCulture);
        if (!UiCatalog.English.TryGetValue(value.Format, out var format))
        {
            MissingEnglish.Add(value.Format);
            format = value.Format;
        }
        var english = string.Format(CultureInfo.CurrentCulture, format, value.GetArguments());
        Remember(chinese, english);
        return Language == "en" ? english : chinese;
    }
    private static void Remember(string chinese, string english)
    {
        Rendered[chinese] = (chinese, english);
        Rendered[english] = (chinese, english);
    }
    internal static string Redisplay(string? rendered)
    {
        if (rendered == null) return "";
        if (Rendered.TryGetValue(rendered, out var pair)) return Language == "en" ? pair.English : pair.Chinese;
        return Text(rendered);
    }
    internal static string RuntimeText(string message)
    {
        if (Language != "en" || string.IsNullOrEmpty(message)) return message;
        if (UiCatalog.English.TryGetValue(message, out var exact)) return exact;

        // Status messages are often built from localized templates at runtime.
        // Match the checked-in templates before falling back to a prefix such as
        // "Could not save: " so exception details remain intact.
        foreach (var (chinese, english) in UiCatalog.English
                     .Where(pair => pair.Key.Contains('{'))
                     .OrderByDescending(pair => pair.Key.Length))
        {
            var placeholders = Regex.Matches(chinese, @"\{(\d+)\}")
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            if (placeholders.Count == 0) continue;
            var pattern = Regex.Escape(chinese);
            var groupByIndex = new Dictionary<int, int>();
            for (var group = 1; group <= placeholders.Count; group++)
            {
                var index = int.Parse(placeholders[group - 1], CultureInfo.InvariantCulture);
                pattern = pattern.Replace(Regex.Escape("{" + index + "}"), "(.*?)", StringComparison.Ordinal);
                groupByIndex[index] = group;
            }
            var match = Regex.Match(message, "^" + pattern + "$", RegexOptions.Singleline);
            if (!match.Success) continue;
            return Regex.Replace(english, @"\{(\d+)\}", replacement =>
            {
                var index = int.Parse(replacement.Groups[1].Value, CultureInfo.InvariantCulture);
                return groupByIndex.TryGetValue(index, out var group) ? match.Groups[group].Value : "";
            });
        }

        foreach (var (chinese, english) in UiCatalog.English
                     .Where(pair => pair.Key.EndsWith('：') && message.StartsWith(pair.Key, StringComparison.Ordinal))
                     .OrderByDescending(pair => pair.Key.Length))
            return english + message[chinese.Length..];
        return message;
    }
    internal static string RedisplayFor(Control owner, string rendered, string slot = "text")
    {
        var captions = Captions.GetOrCreateValue(owner);
        if (!captions.TryGetValue(slot, out var caption))
        {
            caption = new Caption();
            captions[slot] = caption;
        }
        // Keep each control's source: several Chinese captions can legitimately
        // share an English translation, so a global reverse map is ambiguous.
        if (caption.Source.Length == 0 || rendered != caption.Last && rendered != Text(caption.Source))
            caption.Source = UiCatalog.English.ContainsKey(rendered) ? rendered
                : Rendered.TryGetValue(rendered, out var pair) ? pair.Chinese : rendered;
        return caption.Last = Text(caption.Source);
    }
    internal static bool ContainsChinese(string? text) =>
        !string.IsNullOrEmpty(text) && text.Any(ch => ch is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff');
    internal static void Apply(string? language)
    {
        Language = Normalize(language);
        if (Avalonia.Application.Current is not { } app) return;
        foreach (var entry in UiCatalog.Resources) app.Resources[entry.Key] = Text(entry.Value);
        // Native menu items are refreshed explicitly because their lifetime differs from visual controls.
        var icons = TrayIcon.GetIcons(app);
        if (icons is { Count: > 0 })
        {
            icons[0].ToolTipText = "lexi";
            if (icons[0].Menu is { } menu)
            {
                if (menu.Items[0] is NativeMenuItem toggle) toggle.Header = Text("显示 / 隐藏主窗口");
                if (menu.Items[^1] is NativeMenuItem quit) quit.Header = Text("退出 lexi");
            }
        }
    }
    internal static string Value(object? value, string? kind = null)
    {
        if (kind == "encounters") return Format($"遇见 {value} 次 · AI 辅助内容可在编辑档案中修改");
        if (kind == "created") return Format($"录入于 {value}");
        var text = value?.ToString() ?? "";
        // Only domain-generated stage/status fields use this converter; user meaning/notes do not.
        var stage = Regex.Match(text, "^阶段 (\\d+) / 5$");
        if (stage.Success) return Format($"阶段 {stage.Groups[1].Value} / 5");
        stage = Regex.Match(text, "^第 (\\d+) 阶段 \\(共 5 阶段\\)$");
        if (stage.Success) return Format($"第 {stage.Groups[1].Value} 阶段 (共 5 阶段)");
        return Redisplay(text);
    }
}

public sealed class UiValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => UiText.Value(value, parameter?.ToString());
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

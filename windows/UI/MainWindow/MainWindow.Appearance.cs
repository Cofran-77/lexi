using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Styling;

namespace Lexi;

public partial class MainWindow
{
    private bool _loadingAppearance;
    private bool _reduceMotionStyleInstalled;
    private static readonly string[] AppearanceBrushes = ["PaperBrush", "SidebarBrush", "CardBrush", "DialogSurfaceBrush", "InputBg", "InkBrush", "MutedBrush", "LineBrush", "PrimaryGreen", "PrimaryGreenHover", "PrimaryGreenPressed", "AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "OnPrimaryBrush", "TintBrush", "SelectionBrush", "InsetBrush", "InsetBorderBrush", "CardBorderGradient", "PosBrush", "PosVerbBrush", "PosModifierBrush", "PressedTintBrush", "MutedBorderBrush", "FilterSelectedBorderBrush", "FilterSelectedHoverBrush", "FilterSelectedPressedBrush", "CaptionButtonPressedBrush", "DangerBrush", "DangerHoverBrush", "DangerPressedBrush", "DangerBorder", "StudyGradientBrush"];

    private void BindAppearanceEvents()
    {
        foreach (var box in new[] { HighContrastBox, OpaqueMaterialBox, ReduceMotionBox })
            box.IsCheckedChanged += (_, _) =>
            {
                if (_loadingAppearance) return;
                _settings.HighContrast = HighContrastBox.IsChecked == true;
                _settings.OpaqueMaterial = OpaqueMaterialBox.IsChecked == true;
                _settings.ReduceMotion = ReduceMotionBox.IsChecked == true;
                ApplyAppearance();
                try { _vocabService.SaveSettings(_settings); }
                catch (Exception ex) { SetStatus("外观已预览，保存失败：" + ex.Message); }
            };
        SizeChanged += (_, _) => ApplyLayout();
        PageSettings.SizeChanged += (_, _) => SettingsContent.Width = Math.Max(100, PageSettings.Bounds.Width - 16);
    }

    private void LoadAppearance()
    {
        _loadingAppearance = true;
        HighContrastBox.IsChecked = _settings.HighContrast;
        OpaqueMaterialBox.IsChecked = _settings.OpaqueMaterial;
        ReduceMotionBox.IsChecked = _settings.ReduceMotion;
        _loadingAppearance = false;
        ApplyAppearance(); ApplyLayout();
    }

    private void ApplyAppearance()
    {
        if(_glassHost!=null){_glassHost.ForceSolid=_settings.OpaqueMaterial||_settings.HighContrast;_glassHost.Refresh();}
        foreach (var key in AppearanceBrushes) Resources.Remove(key);
        var dark = _settings.Theme == "Dark";
        if (_settings.HighContrast)
        {
            void Brush(string key, string color) => Resources[key] = new SolidColorBrush(Color.Parse(color));
            var bg = dark ? "#101010" : "#FFFFFF";
            var fg = dark ? "#FFFFFF" : "#000000";
            foreach (var key in new[] { "PaperBrush", "SidebarBrush", "CardBrush", "DialogSurfaceBrush", "InputBg", "InsetBrush" }) Brush(key, bg);
            foreach (var key in new[] { "InkBrush", "MutedBrush", "LineBrush", "InsetBorderBrush", "CardBorderGradient" }) Brush(key, fg);
            foreach (var key in new[] { "PrimaryGreen", "PrimaryGreenHover", "PrimaryGreenPressed", "AccentBrush", "AccentHoverBrush", "AccentPressedBrush" }) Brush(key, dark ? "#AAE8FF" : "#003E67");
            Brush("OnPrimaryBrush", dark ? "#000000" : "#FFFFFF");
            Brush("TintBrush", dark ? "#202020" : "#EEEEEE");
            Brush("SelectionBrush", dark ? "#303030" : "#DDDDDD");
            foreach (var key in new[] { "PosBrush", "PosVerbBrush", "PosModifierBrush" }) Brush(key, bg);
            Brush("PressedTintBrush", dark ? "#303030" : "#DDDDDD");
            Brush("MutedBorderBrush", fg);
            Brush("FilterSelectedBorderBrush", fg);
            Brush("FilterSelectedHoverBrush", dark ? "#404040" : "#CCCCCC");
            Brush("FilterSelectedPressedBrush", dark ? "#505050" : "#BBBBBB");
            Brush("CaptionButtonPressedBrush", dark ? "#303030" : "#DDDDDD");
            Brush("DangerBrush", dark ? "#FF6B6B" : "#B00020");
            Brush("DangerHoverBrush", dark ? "#FF8E8E" : "#D32F2F");
            Brush("DangerPressedBrush", dark ? "#FF5252" : "#8E0000");
            Brush("DangerBorder", dark ? "#FF6B6B" : "#B00020");
        }
        else if (_settings.OpaqueMaterial)
        {
            Resources["PaperBrush"] = new SolidColorBrush(Color.Parse(dark ? "#172833" : "#F3F8FB"));
            Resources["CardBrush"] = new SolidColorBrush(Color.Parse(dark ? "#21323E" : "#FFFFFF"));
            Resources["SidebarBrush"] = new SolidColorBrush(Color.Parse(dark ? "#1B303D" : "#EAF4F8"));
            Resources["InputBg"] = Resources["CardBrush"];
        }
        Resources["StudyGradientBrush"] = _settings.HighContrast
            ? Resources["PaperBrush"]
            : new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(dark ? "#193447" : "#EDF6F9"), 0),
                    new GradientStop(Color.Parse(dark ? "#24445C" : "#C6E2ED"), 0.52),
                    new GradientStop(Color.Parse(dark ? "#18364B" : "#E4F1F6"), 1)
                }
            };
        TransparencyLevelHint = _settings.OpaqueMaterial || _settings.HighContrast
            ? [WindowTransparencyLevel.None] : [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.None];
        Classes.Set("reduce-motion", _settings.ReduceMotion);
        EnsureReduceMotionTransitionsOverride();
        // MainWindow.axaml pins the sidebar background locally; the theme owns the Glacier tone.
        SidebarBorder.ClearValue(Border.BackgroundProperty);
        foreach (var key in new[] { "SoftCardShadow", "FloatingShadow", "FocusGlowShadow", "DrawerShadow" })
        {
            Resources.Remove(key);
            if (_settings.HighContrast || _settings.OpaqueMaterial) Resources[key] = default(BoxShadows);
        }
    }

    // Transitions declared by styles loaded after VisualTheme.axaml win app-level
    // reduce-motion rules, so the same switches are re-asserted on the window layer.
    private void EnsureReduceMotionTransitionsOverride()
    {
        if (_reduceMotionStyleInstalled) return;
        _reduceMotionStyleInstalled = true;
        AddTransitionsOverride(x => x.OfType<Window>().Class("reduce-motion").Descendant().OfType<Border>().Class("plan-card"));
        AddTransitionsOverride(x => x.OfType<Window>().Class("reduce-motion").Descendant().OfType<Button>().Class("settings-section-card"));
        AddTransitionsOverride(x => x.OfType<Window>().Class("reduce-motion").Descendant().OfType<Button>().Class("archive-menu-item"));
        AddTransitionsOverride(x => x.OfType<Window>().Class("reduce-motion").Descendant().OfType<Button>());
        AddTransitionsOverride(x => x.OfType<Window>().Class("reduce-motion").Descendant().OfType<Border>().Name("ButtonSurface"));
        AddTransitionsOverride(x => x.OfType<Window>().Class("reduce-motion").Descendant().OfType<CheckBox>());
        AddTransitionsOverride(x => x.OfType<Window>().Class("reduce-motion").Descendant().OfType<ToggleButton>());
        AddTransitionsOverride(x => x.OfType<Window>().Class("reduce-motion").Descendant().OfType<TextBox>());
    }

    private void AddTransitionsOverride(Func<Selector, Selector> selector)
    {
        var style = new Style(parent => selector(parent!));
        style.Add(new Setter(Animatable.TransitionsProperty, null));
        Styles.Add(style);
    }

    private void ApplyLayout()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : Width;
        var compact = width < 960;
        Classes.Set("compact", compact);
        Classes.Set("medium", width >= 960 && width < 1280);
        Classes.Set("expanded", width >= 1280);
        PageReview.Margin = new Thickness(compact ? 18 : 32, 24);
        PageSettings.Margin = new Thickness(compact ? 18 : 36, 24);
        UpdateExamTopicsResponsiveLayout(compact);
        var renderer = UiText.Text(Environment.GetEnvironmentVariable("LEXI_SOFTWARE_RENDERING") == "0"
            ? "硬件优先"
            : "软件");
        var hotkey = UiText.Text(App.Hotkey?.IsRegistered == true ? "已注册" : "未注册或被占用");
        DiagnosticsText.Text = UiText.Format(
            $"Lexi {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · Windows\n布局：{(compact ? "Compact" : width < 1280 ? "Medium" : "Expanded")} · 显示缩放 {RenderScaling * 100:0}%\n渲染：{renderer} · 数据仅存本机\nAlt+D：{hotkey}");
    }
}

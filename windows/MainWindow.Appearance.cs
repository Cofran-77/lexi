using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Animation;

namespace Lexi;

public partial class MainWindow
{
    private bool _loadingAppearance;
    private static readonly string[] AppearanceBrushes = ["PaperBrush", "CardBrush", "DialogSurfaceBrush", "InputBg", "InkBrush", "MutedBrush", "LineBrush", "PrimaryGreen", "PrimaryGreenHover", "PrimaryGreenPressed", "OnPrimaryBrush", "TintBrush", "SelectionBrush", "InsetBrush", "InsetBorderBrush", "CardBorderGradient"];

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
        foreach (var key in AppearanceBrushes) Resources.Remove(key);
        var dark = _settings.Theme == "Dark";
        if (_settings.HighContrast)
        {
            void Brush(string key, string color) => Resources[key] = new SolidColorBrush(Color.Parse(color));
            var bg = dark ? "#101010" : "#FFFFFF";
            var fg = dark ? "#FFFFFF" : "#000000";
            foreach (var key in new[] { "PaperBrush", "CardBrush", "DialogSurfaceBrush", "InputBg", "InsetBrush" }) Brush(key, bg);
            foreach (var key in new[] { "InkBrush", "MutedBrush", "LineBrush", "InsetBorderBrush", "CardBorderGradient" }) Brush(key, fg);
            foreach (var key in new[] { "PrimaryGreen", "PrimaryGreenHover", "PrimaryGreenPressed" }) Brush(key, dark ? "#AAE8FF" : "#003E67");
            Brush("OnPrimaryBrush", dark ? "#000000" : "#FFFFFF");
            Brush("TintBrush", dark ? "#202020" : "#EEEEEE");
            Brush("SelectionBrush", dark ? "#303030" : "#DDDDDD");
        }
        else if (_settings.OpaqueMaterial)
        {
            Resources["PaperBrush"] = new SolidColorBrush(Color.Parse(dark ? "#18191C" : "#F4F7F5"));
            Resources["CardBrush"] = new SolidColorBrush(Color.Parse(dark ? "#202024" : "#FFFFFF"));
            Resources["InputBg"] = Resources["CardBrush"];
        }
        TransparencyLevelHint = _settings.OpaqueMaterial || _settings.HighContrast
            ? [WindowTransparencyLevel.None] : [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.None];
        Classes.Set("reduce-motion", _settings.ReduceMotion);
        foreach (var key in new[] { "SoftCardShadow", "FloatingShadow", "FocusGlowShadow", "DrawerShadow" })
        {
            Resources.Remove(key);
            if (_settings.HighContrast || _settings.OpaqueMaterial) Resources[key] = default(BoxShadows);
        }
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
        DiagnosticsText.Text = $"Lexi {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · Windows\n布局：{(compact ? "Compact" : width < 1280 ? "Medium" : "Expanded")} · 显示缩放 {RenderScaling * 100:0}%\n渲染：{(Environment.GetEnvironmentVariable("LEXI_SOFTWARE_RENDERING") == "0" ? "硬件优先" : "软件")} · 数据仅存本机\nAlt+D：{(App.Hotkey?.IsRegistered == true ? "已注册" : "未注册或被占用")}";
    }
}

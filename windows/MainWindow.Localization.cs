using Avalonia.Controls;
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
                SetStatus("界面语言未能保存：" + ex.Message);
                return;
            }
        }
        LoadLanguage();
    }

    private void ApplyUiLanguage()
    {
        UiText.Apply(_settings.UiLanguage);
        RefreshIeltsLanguage();
        LanguageToggleBtn.Content = _settings.UiLanguage == "en" ? "中" : "EN";
        Title = UiText.Redisplay(Title);
        foreach (var control in this.GetLogicalDescendants().OfType<Control>())
        {
            switch (control)
            {
                case TextBlock label when !string.IsNullOrEmpty(label.Text):
                    label.Text = UiText.Redisplay(label.Text);
                    break;
                case Button button when button.Content is string caption:
                    button.Content = UiText.Redisplay(caption);
                    break;
                case CheckBox box when box.Content is string caption:
                    box.Content = UiText.Redisplay(caption);
                    break;
                case TextBox input when !string.IsNullOrEmpty(input.Watermark):
                    input.Watermark = UiText.Redisplay(input.Watermark);
                    break;
            }
        }
        foreach (var combo in new[] { SettingsThemeCombo, SettingsProviderCombo, SettingsProtocolCombo,
                     SettingsContextCombo, SettingsTimeoutCombo, VocabStatusFilter, BatchActionCombo })
            foreach (var item in combo.Items.OfType<ComboBoxItem>())
                if (item.Content is string caption) item.Content = UiText.Redisplay(caption);
    }
}

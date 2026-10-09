using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using System.Reflection;

namespace Lexi;

public static class LanguageUiTests
{
    public static async Task RunAsync(MainWindow main)
    {
        var report = new List<string>(); var exit = 0;
        void Check(bool value, string name) { report.Add((value ? "PASS " : "FAIL ") + name); if (!value) throw new Exception(name); }
        var folder = Environment.GetEnvironmentVariable("LEXI_DATA_DIR")!;
        try
        {
            Check(main.FindControl<ComboBox>("SettingsLanguageCombo") != null, "settings provides a language selector");
            var method = typeof(MainWindow).GetMethod("SetUiLanguage");
            Check(method != null, "language switching API is available");
            main.FindControl<TextBox>("LookupInput")!.Text = "resilient";
            main.FindControl<TextBox>("ArchiveNotesInput")!.Text = "用户备注：学习中";
            method!.Invoke(main, ["en"]);
            await Task.Delay(100);
            Check(main.FindControl<Button>("LookupBtn")!.Content?.ToString() == "Look up ↵", "English updates existing lookup controls");
            Check(main.FindControl<Button>("BackupNowBtn")!.Content?.ToString() == "Back up now", "English updates backup controls");
            Check(main.FindControl<TextBox>("LookupInput")!.Text == "resilient" && main.FindControl<TextBox>("ArchiveNotesInput")!.Text == "用户备注：学习中", "language switching preserves user text");
            using var store = new VocabularyService();
            Check(store.LoadSettings().GetType().GetProperty("UiLanguage")!.GetValue(store.LoadSettings())?.ToString() == "en", "switching persists English");
            main.FindControl<Button>("SettingsSaveBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.LoadSettings().GetType().GetProperty("UiLanguage")!.GetValue(store.LoadSettings())?.ToString() == "en", "saving AI settings preserves English");
            var combo = main.FindControl<ComboBox>("SettingsLanguageCombo")!;
            combo.SelectedIndex = 0;
            Check(main.FindControl<Button>("LookupBtn")!.Content?.ToString() == "查询 ↵", "actual selector returns to Chinese");
        }
        catch (Exception ex) { report.Add(ex.ToString()); exit = 1; }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(folder, "language-test-result.txt"), report);
            foreach (var line in report) Console.WriteLine(line);
            main.ForceClose(); (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(exit);
        }
    }
}

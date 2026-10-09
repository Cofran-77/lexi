using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Lexi.Features.Ielts;

namespace Lexi;

public partial class MainWindow
{
    private IeltsCatalog? _ieltsCatalog;
    private Grid? _ieltsPage;
    private StackPanel? _ieltsSubContent;
    private ScrollViewer? _ieltsSubContentPage;
    private IeltsWorkspaceControl? _ieltsWorkspace;
    private Button? _ieltsNav;
    private readonly HashSet<string> _ieltsSelected = [];
    private LearningProgress _ieltsProgress = new();
    private string? _ieltsProgressPath;
    private bool _ieltsProgressBlocked;
    private LocalWordAudioPlayer? _learningAudio;

    private LocalWordAudioPlayer GetLearningAudio() => _learningAudio ??= new LocalWordAudioPlayer(SetStatus);

    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
    private void SystemFeedbackSound(bool correct) => MessageBeep(correct ? 0x40u : 0x30u);

    private void InitializeIelts()
    {
        // IELTS 根页面容器（Grid，包含主工作区与子页面宿主）
        _ieltsPage = new Grid { Name = "IeltsPage", IsVisible = false };

        // 子页面容器（用于听力资料、100句写作、同义替换听写）
        _ieltsSubContent = new StackPanel { Spacing = 16, Margin = new Thickness(28, 24), MaxWidth = 1040 };
        _ieltsSubContentPage = new ScrollViewer
        {
            Name = "IeltsSubContentPage",
            Content = _ieltsSubContent,
            IsVisible = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        _ieltsPage.Children.Add(_ieltsSubContentPage);

        this.FindControl<Panel>("PagesHost")!.Children.Add(_ieltsPage);

        // 导航按钮
        _ieltsNav = new Button { Name = "NavIelts", Content = IeltsI18n.T("IELTS 专题") };
        _ieltsNav.Classes.Add("nav");
        _ieltsNav.HorizontalAlignment = HorizontalAlignment.Stretch;
        _ieltsNav.Click += (_, _) => ShowIeltsCatalog();
        this.FindControl<StackPanel>("LearningNavHost")!.Children.Add(_ieltsNav);

        Closed += (_, _) =>
        {
            _ieltsWorkspace?.Dispose();
            _learningAudio?.Dispose();
            StopWindowsLearningAudio();
        };

        LoadIeltsProgress();

        // 绑定语言切换事件，支持动态即时刷新
        LanguageToggleBtn.Click += (_, _) => Dispatcher.UIThread.Post(RefreshIeltsLanguage);
        SettingsLanguageCombo.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(RefreshIeltsLanguage);

        if (Environment.GetEnvironmentVariable("LEXI_IELTS_TEST") == "1")
        {
            IeltsTests.RunTests();
        }
    }

    private void LoadIeltsProgress()
    {
        _ieltsProgressPath = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "learning-progress.json");
        try
        {
            _ieltsProgress = LearningProgress.Load(_ieltsProgressPath);
            _ieltsProgressBlocked = false;
        }
        catch (Exception ex)
        {
            _ieltsProgress = new();
            _ieltsProgressBlocked = true;
            SetStatus("学习记录无法读取，原文件已保留：" + ex.Message);
        }
    }

    private void SaveIeltsProgress()
    {
        if (_restoring || !_databaseAvailable || _ieltsProgressBlocked) return;
        var path = Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "learning-progress.json");
        if (path != _ieltsProgressPath) { LoadIeltsProgress(); return; }
        try { _ieltsProgress.Save(path); }
        catch (Exception ex) { SetStatus("学习记录保存失败：" + ex.Message); }
    }

    private void ShowIeltsCatalog()
    {
        ShowPage("ielts");

        if (_ieltsPage == null) return;

        try { _ieltsCatalog ??= IeltsCatalog.Load(); }
        catch (Exception ex)
        {
            SetStatus("IELTS 资源加载失败：" + ex.Message);
            return;
        }

        if (_ieltsWorkspace == null)
        {
            _ieltsWorkspace = new IeltsWorkspaceControl(
                catalog: _ieltsCatalog,
                progress: _ieltsProgress,
                sharedSelection: _ieltsSelected,
                playerProvider: GetLearningAudio,
                setStatus: SetStatus,
                startTyping: (words, hints) => StartLearningTyping(words, hints),
                createPlan: section => OpenPlanCreator(DailyStudyPlanSource.Ielts, section),
                startSynonyms: words => StartIeltsSynonyms(words),
                openResources: ShowIeltsResources,
                openWriting: ShowIeltsWriting
            );

            _ieltsPage.Children.Add(_ieltsWorkspace);
            _ieltsWorkspace.ProgressChanged += SaveIeltsProgress;
        }

        // 显示主工作区，隐藏子页面
        _ieltsSubContentPage!.IsVisible = false;
        _ieltsWorkspace.IsVisible = true;
    }

    public void SelectIeltsSection(LearningSection section)
    {
        ShowIeltsCatalog();
        _ieltsWorkspace?.SelectSection(section);
        _ieltsProgress.SelectedSection = section.Id;
        SaveIeltsProgress();
    }

    public void RefreshIeltsLanguage()
    {
        if (_ieltsNav != null)
            _ieltsNav.Content = IeltsI18n.T("IELTS 专题");

        _ieltsWorkspace?.RefreshLanguage();
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, System.Text.StringBuilder? result, int length, IntPtr callback);

    private void PlayWindowsLearningAudio(string relative)
    {
        var file = IeltsCatalog.ResolveAsset(relative);
        if (file == null) { SetStatus(IeltsI18n.T("该条目暂无录音。")); return; }
        GetLearningAudio().Play("", file);
    }

    private void StopWindowsLearningAudio() => _learningAudio?.Stop();
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;

namespace Lexi;

/// <summary>Focused study surface shared by lookup and archive decks.</summary>
public partial class MainWindow
{
    private Border? _focusHost;
    private Button? _focusEntry;
    private TextBlock? _focusWord, _focusMeaning, _focusProgress, _focusFeedback, _focusPhonetic;
    private TextBlock? _focusDetails;
    private StackPanel? _focusActions;
    private StackPanel? _focusTopActions;
    private StackPanel? _focusStreak;
    private StudyRound<string>? _focusRound;
    private string? _focusWordId;
    private bool _focusAnswerVisible;
    private bool _focusRated;
    private StudyRating _focusLastRating;
    private StudyRound<string>.Checkpoint? _focusUndo;
    private long? _focusUndoArchiveId;
    private int? _focusUndoRevision;
    private bool _focusUndoIsRating;
    private string _focusPreviousPage = "lookup";
    private bool _focusActive;
    private string? _focusRatedWord;
    private string _focusOriginalTranslation = "";

    private void InitializeFocus()
    {
        if (_focusHost != null) return;
        _focusEntry = new Button { Name = "FocusEntryButton", Content = "专注学习", Padding = new Thickness(12, 7) };
        _focusEntry.Classes.Add("secondary"); _focusEntry.Click += (_, _) => EnterFocusFromLookup();
        if (LookupResultCard.Child is Panel headerHost)
        {
            headerHost.Children.Add(_focusEntry);
        }
        else
        {
            _focusEntry.IsVisible = false;
        }

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(36, 26, 36, 42) };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var back = FocusButton("返回", ExitFocus);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(back);
        _focusProgress = new TextBlock { Name = "FocusProgress", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(_focusProgress, 1); top.Children.Add(_focusProgress);
        _focusStreak = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_focusStreak, 2); top.Children.Add(_focusStreak);
        _focusTopActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_focusTopActions, 3); top.Children.Add(_focusTopActions);
        layout.Children.Add(top);

        var studyScroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
        var card = new StackPanel { Spacing = 18, MaxWidth = 680, Margin = new Thickness(0, 70, 0, 24), HorizontalAlignment = HorizontalAlignment.Center };
        _focusWord = new TextBlock { Name = "FocusWord", FontSize = 46, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
        _focusPhonetic = new TextBlock { FontSize = 18, Opacity = .72, TextWrapping = TextWrapping.Wrap };
        _focusMeaning = new TextBlock { Name = "FocusMeaning", FontSize = 23, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _focusDetails = new TextBlock { Name = "FocusDetails", FontSize = 16, TextWrapping = TextWrapping.Wrap, Opacity = .86 };
        card.Children.Add(_focusWord); card.Children.Add(_focusPhonetic); card.Children.Add(_focusMeaning); card.Children.Add(_focusDetails);
        studyScroll.Content = card; Grid.SetRow(studyScroll, 1); layout.Children.Add(studyScroll);

        var bottom = new StackPanel { Spacing = 20, HorizontalAlignment = HorizontalAlignment.Center };
        _focusFeedback = new TextBlock { Name = "FocusFeedback", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Opacity = .68 };
        _focusActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        bottom.Children.Add(_focusFeedback); bottom.Children.Add(_focusActions);
        Grid.SetRow(bottom, 2); layout.Children.Add(bottom);
        _focusHost = new Border { Name = "FocusHost", Child = layout, IsVisible = false };
        _focusHost.Bind(Border.BackgroundProperty, this.GetResourceObservable("PaperBrush"));
        Grid.SetRow(_focusHost, 1);
        _focusHost.SetValue(Panel.ZIndexProperty, 100);
        ((Grid)RootWindowBorder.Child!).Children.Add(_focusHost);
        var archiveFocus = LearningButton("选中词汇专注学习", EnterArchiveFocus);
        archiveFocus.Name = "ArchiveFocusButton";
        if (this.FindControl<Panel>("VocabFocusActionHost") is { } actionHost) actionHost.Children.Add(archiveFocus);
        VocabListBox.DoubleTapped += (_, e) =>
        {
            if (e.Source is Control control && control.DataContext is WordItem item) EnterArchiveFocusWithFirst(item);
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!_focusActive || e.Source is TextBox) return;
            if (e.Key == Key.Escape) { ExitFocus(); e.Handled = true; }
            else if (e.Key == Key.A) { GetLearningAudio().Play(_focusRated ? _focusRatedWord ?? "" : _focusRound?.HasCurrent == true ? _focusRound.Current : ""); e.Handled = true; }
            else if (e.Key == Key.C) { SaveFocusCurrent(); e.Handled = true; }
            else if (e.Key == Key.Delete) { MasterFocus(); e.Handled = true; }
            else if (e.Key is Key.Enter or Key.S) { HandleFocusSpace(); e.Handled = true; }
            else if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Alt) { UndoFocus(); e.Handled = true; }
            else if (e.Key == Key.Space) { HandleFocusSpace(); e.Handled = true; }
            else if (e.Key == Key.Q && _focusAnswerVisible && !_focusRated) { RateFocus(StudyRating.Known); e.Handled = true; }
            else if (e.Key == Key.W && _focusAnswerVisible && !_focusRated) { RateFocus(StudyRating.Unsure); e.Handled = true; }
            else if (e.Key == Key.E && _focusAnswerVisible && !_focusRated) { RateFocus(StudyRating.Forgot); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }

    private void EnterFocusFromLookup()
    {
        if (_focusActive || _restoring || !_databaseAvailable || !LookupResultCard.IsVisible) return;
        var text = ResultWordText.Text?.Trim(); if (string.IsNullOrWhiteSpace(text)) return;
        var archive = _allWords.FirstOrDefault(w => w.Word.Equals(text, StringComparison.OrdinalIgnoreCase));
        _focusWordId = archive?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? text;
        _focusRound = new StudyRound<string>(); _focusRound.Reset([text], archive == null ? StudyMode.FirstLearn : StudyMode.Review, shuffle: false);
        _focusPreviousPage = _currentPage; _focusActive = true; _focusAnswerVisible = false; _focusRated = false; _focusUndo = null; _focusUndoArchiveId = null; _focusUndoRevision = null;
        _focusOriginalTranslation = ResultTranslationText.Text ?? "";
        BeginFocusMemory(_focusRound.Mode, _focusRound.Total);
        PageLookup.IsVisible = false; LookupPageHost.IsVisible = false; _learningHubPage.IsVisible = false; _focusHost!.IsVisible = true;
        RenderFocus();
    }

    private void ExitFocus()
    {
        if (!_focusActive) return;
        PersistLearningSurface("focus");
        _focusActive = false; _focusHost!.IsVisible = false; PageLookup.IsVisible = _focusPreviousPage == "lookup"; LookupPageHost.IsVisible = _focusPreviousPage == "lookup";
        PageVocab.IsVisible = _focusPreviousPage == "vocab"; PageReview.IsVisible = _focusPreviousPage == "review"; PageSettings.IsVisible = _focusPreviousPage == "settings"; _learningHubPage.IsVisible = _focusPreviousPage == "learning";
        if (_ieltsPage != null) _ieltsPage.IsVisible = _focusPreviousPage == "ielts";
        if (_quotesPage != null) _quotesPage.IsVisible = _focusPreviousPage == "quotes";
        _focusEntry?.Focus(); SetStatus("已退出专注学习，当前词库进度已保留。");
    }

    private void RenderFocus()
    {
        if (!_focusActive || _focusRound == null) return;
        _focusActions!.Children.Clear();
        _focusTopActions!.Children.Clear();
        _focusProgress!.Text = $"专注学习 · 已完成 {_focusRound.Completed}/{_focusRound.Total} · 连击 {_focusRound.CurrentStreak}/{_focusRound.CurrentTarget}";
        _focusStreak!.Children.Clear();
        for (var index = 0; index < StudyRound<string>.RequiredStreak; index++)
            _focusStreak.Children.Add(new Border { Width = 16, Height = 6, CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Avalonia.Media.Color.Parse(index < _focusRound.CurrentStreak ? "#245FAD" : "#8AAFC5")) });
        var current = _focusRated ? _focusRatedWord ?? "" : _focusRound.HasCurrent ? _focusRound.Current : "";
        if (_focusRound.HasCurrent && !_focusRated) PresentFocusMemory(current, _focusRound.CurrentStep);
        _focusWord!.Text = current;
        if (_focusRound.HasCurrent && _focusRound.CurrentStep == StudyStep.Learn) _focusAnswerVisible = true;
        var archive = _allWords.FirstOrDefault(w => w.Word.Equals(current, StringComparison.OrdinalIgnoreCase));
        _focusPhonetic!.Text = archive?.Phonetic ?? (string.Equals(ResultWordText.Text, current, StringComparison.OrdinalIgnoreCase) ? ResultPhoneticText.Text ?? "" : "");
        _focusMeaning!.Text = _focusAnswerVisible ? archive?.Translation ?? _focusOriginalTranslation : "先回忆这个词的含义";
        _focusMeaning.IsVisible = _focusAnswerVisible;
        _focusDetails!.Text = archive == null
            ? string.Equals(ResultWordText.Text, current, StringComparison.OrdinalIgnoreCase) ? ResultDefinitionText.Text ?? "" : ""
            : string.Join("\n", new[] { archive.Definition }.Concat(archive.AiExamples.Select(e => e.English + "\n" + e.Chinese)));
        _focusDetails.IsVisible = _focusAnswerVisible;
        PersistLearningSurface("focus");
        _focusFeedback!.Text = _focusRound.IsFinished ? "本轮已完成。你可以退出专注。" : _focusRound.CurrentStep == StudyStep.Learn ? "学习卡：看过释义后开始回忆。" : _focusRated ? (_focusLastRating == StudyRating.Known ? "已记为认识。" : "稍后会再次出现。") : "先自己回忆，再揭晓释义。";
        _focusTopActions.Children.Add(FocusButton("朗读", () => GetLearningAudio().Play(current)));
        if (_focusRound.HasCurrent && !_focusRated)
        {
            _focusTopActions.Children.Add(FocusButton("收藏", SaveFocusCurrent));
            _focusTopActions.Children.Add(FocusButton("已掌握", MasterFocus));
        }
        if (!_focusRound.HasCurrent)
        {
            if (_focusUndo != null) _focusActions.Children.Add(FocusButton("撤销", UndoFocus));
            if (_focusRated && _focusLastRating != StudyRating.Forgot) _focusActions.Children.Add(FocusButton("记错了，改为忘记", ReclassifyFocus));
            return;
        }
        if (_focusRated)
        {
            _focusActions.Children.Add(FocusButton("撤销", UndoFocus));
            if (_focusLastRating != StudyRating.Forgot) _focusActions.Children.Add(FocusButton("记错了，改为忘记", ReclassifyFocus));
            _focusActions.Children.Add(FocusButton("下一词", () => { _focusRated = false; _focusAnswerVisible = false; RenderFocus(); }, true));
            return;
        }
        if (_focusRound.CurrentStep == StudyStep.Learn)
        {
            _focusAnswerVisible = true; _focusMeaning.IsVisible = true;
            _focusActions.Children.Add(FocusButton("看完了，开始回忆", () => { _focusRound.CompleteLearn(); _focusPresentation = null; _focusAnswerVisible = false; _focusRated = false; RenderFocus(); }, true));
        }
        else if (!_focusAnswerVisible)
            _focusActions.Children.Add(FocusButton("揭晓释义", () => { _focusAnswerVisible = true; RenderFocus(); }, true));
        else if (!_focusRated)
        {
            _focusActions.Children.Add(FocusButton("认识 (Q)", () => RateFocus(StudyRating.Known)));
            _focusActions.Children.Add(FocusButton("模糊 (W)", () => RateFocus(StudyRating.Unsure)));
            _focusActions.Children.Add(FocusButton("忘记 (E)", () => RateFocus(StudyRating.Forgot)));
        }
        else
        {
            _focusActions.Children.Add(FocusButton("撤销", UndoFocus));
            if (!_focusRound.IsFinished) _focusActions.Children.Add(FocusButton("下一词", () => { _focusRated = false; _focusAnswerVisible = false; RenderFocus(); }, true));
        }
    }

    private Button FocusButton(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = label, Padding = new Thickness(14, 9) }; b.Classes.Add(primary ? "primary" : "secondary"); b.Click += (_, _) => action(); return b;
    }

    private void RateFocus(StudyRating rating)
    {
        if (_restoring || !_databaseAvailable || !_focusActive || _focusRound == null || !_focusRound.HasCurrent || _focusRound.CurrentStep != StudyStep.Recall || !_focusAnswerVisible || _focusRated) return;
        var checkpoint = _focusRound.CaptureCheckpoint(); var word = _focusRound.Current;
        var memoryRated = false;
        try
        {
            var before = _focusRound.CurrentStreak;
            var result = _focusRound.Commit(rating); var archive = _allWords.FirstOrDefault(w => w.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
            RateFocusMemory(rating, before, result.Streak, result.Completed, _focusRound.Mode);
            memoryRated = true;
            _focusUndoIsRating = true;
            RefreshWords();
            _focusUndo = checkpoint; _focusUndoArchiveId = null; _focusUndoRevision = null; _focusLastRating = rating; _focusRated = true; _focusRatedWord = word;
            PersistLearningSurface("focus");
            SetStatus(rating == StudyRating.Known ? "已记录认识。" : rating == StudyRating.Unsure ? "已记录模糊，将再次出现。" : "已记录忘记，将重新学习。"); RenderFocus();
        }
        catch (Exception ex) { if (memoryRated) { try { UndoFocusMemory(); } catch (Exception undoError) { SetStatus("专注回滚失败：" + undoError.Message); return; } } checkpoint.Restore(); SetStatus("专注评分失败：" + ex.Message); RenderFocus(); }
    }

    private void UndoFocus()
    {
        if (_restoring || !_databaseAvailable || !_focusActive || _focusRound == null || _focusUndo == null) return;
        try
        {
            if (_focusUndoArchiveId is { } id)
            {
                var current = _vocabService.GetAllWords().FirstOrDefault(w => w.Id == id);
                if (_focusUndoRevision is { } rev && current?.Archive.Revision != rev)
                    throw new InvalidOperationException("该词已有新的变更，无法撤销。");
            }
            if (_focusUndoIsRating) UndoFocusMemory();
            if (_focusUndoArchiveId is { } undoId && !_vocabService.UndoLastLearningAction(undoId))
                throw new InvalidOperationException("该词已有新的变更，无法撤销。");
            _focusUndo.Restore(); _focusUndo = null; _focusUndoArchiveId = null; _focusUndoRevision = null; _focusRated = false; _focusAnswerVisible = true; RefreshWords(); RenderFocus(); SetStatus("已撤销上一次专注评分。");
            PersistLearningSurface("focus");
        }
        catch (Exception ex) { SetStatus("撤销失败：" + ex.Message); }
    }

    private void HandleFocusSpace()
    {
        if (_restoring || !_databaseAvailable || !_focusActive) return;
        if (_focusRound == null || !_focusRound.HasCurrent) return;
        if (_focusRated) { _focusRated = false; _focusAnswerVisible = false; RenderFocus(); return; }
        if (_focusRound.CurrentStep == StudyStep.Learn)
        {
            _focusRound.CompleteLearn(); _focusPresentation = null; _focusAnswerVisible = false; _focusRated = false; RenderFocus(); return;
        }
        if (!_focusAnswerVisible) { _focusAnswerVisible = true; RenderFocus(); return; }
        if (_focusRated) { _focusRated = false; _focusAnswerVisible = false; RenderFocus(); return; }
        RateFocus(StudyRating.Known);
    }

    private void MasterFocus()
    {
        if (_restoring || !_databaseAvailable || !_focusActive || _focusRound?.HasCurrent != true || _focusRated) return;
        var word = _focusRound.Current; var checkpoint = _focusRound.CaptureCheckpoint();
        try
        {
            var item = _allWords.FirstOrDefault(w => w.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (item == null) { SaveFocusCurrent(); item = _allWords.FirstOrDefault(w => w.Word.Equals(word, StringComparison.OrdinalIgnoreCase)); }
            if (item == null) throw new InvalidOperationException("收藏单词失败。");
            _vocabService.ExecuteBatch([item.Id], "master"); _focusRound.CompleteCurrent(); RefreshWords();
            _focusUndoIsRating = false;
            _focusUndo = checkpoint; _focusUndoArchiveId = item.Id; _focusUndoRevision = _vocabService.GetAllWords().Single(w => w.Id == item.Id).Archive.Revision; _focusLastRating = StudyRating.Known; _focusRated = true; _focusRatedWord = word; _focusAnswerVisible = true; RenderFocus();
        }
        catch (Exception ex) { checkpoint.Restore(); SetStatus("标记掌握失败：" + ex.Message); }
    }
    private void EnterArchiveFocus() => EnterArchiveFocusWithFirst(null);
    private void EnterArchiveFocusWithFirst(WordItem? first)
    {
        if (_restoring || !_databaseAvailable || _focusActive) return;
        var words = _allWords.Where(w => w.Selected).ToList(); if (words.Count == 0) words = _allWords.ToList();
        if (first != null) { words.RemoveAll(w => w.Id == first.Id); words.Insert(0, first); }
        if (words.Count == 0) { SetStatus("词汇档案为空。"); return; }
        _focusPreviousPage = _currentPage; _focusRound = new StudyRound<string>(); _focusRound.Reset(words.Select(w => w.Word), StudyMode.Review);
        _focusActive = true; _focusAnswerVisible = false; _focusRated = false; _focusUndo = null; _focusUndoArchiveId = null; _focusUndoRevision = null;
        BeginFocusMemory(_focusRound.Mode, _focusRound.Total);
        PageLookup.IsVisible = false; LookupPageHost.IsVisible = false; PageVocab.IsVisible = false; PageReview.IsVisible = false; PageSettings.IsVisible = false; _learningHubPage.IsVisible = false;
        if (_ieltsPage != null) _ieltsPage.IsVisible = false;
        if (_quotesPage != null) _quotesPage.IsVisible = false;
        _focusHost!.IsVisible = true; RenderFocus();
    }
    private void ReclassifyFocus()
    {
        if (!_focusRated || _focusLastRating == StudyRating.Forgot || _focusUndo == null) return;
        UndoFocus(); if (_focusUndo == null) RateFocus(StudyRating.Forgot);
    }
    private void SaveFocusCurrent()
    {
        if (_restoring || !_databaseAvailable || !_focusActive || _focusRound?.HasCurrent != true) return;
        var word = _focusRound.Current;
        if (_allWords.Any(w => w.Word.Equals(word, StringComparison.OrdinalIgnoreCase))) { SetStatus("该词已收藏。"); return; }
        if (!string.Equals(ResultWordText.Text, word, StringComparison.OrdinalIgnoreCase))
        { SetStatus("当前词已不在档案中，请从查词页重新查询后收藏。"); return; }
        _vocabService.AddWord(word, ResultPhoneticText.Text ?? "", _focusOriginalTranslation, ResultDefinitionText.Text ?? ""); RefreshWords(); SetStatus("当前专注词已收藏。");
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Diagnostics;

namespace Lexi;

public partial class MainWindow
{
    // Keep the persisted post-rating revision so explicit mutations (undo/reschedule) can
    // make the word eligible again during the same calendar day.
    private readonly Dictionary<long, int> _reviewHandled = [];
    private readonly ReviewSession _reviewSession = new();
    private DateTime _reviewDay = DateTime.Today;
    private WordItem? _reviewWord;
    private bool _reviewRevealed;
    private bool _reviewBusy;
    private int _reviewEpoch;

    private void BindReviewEvents()
    {
        ReviewRevealBtn.Click += (_, _) => RevealReview();
        ReviewRememberBtn.Click += async (_, _) => await RateReviewAsync(true);
        ReviewUnfamiliarBtn.Click += async (_, _) => await RateReviewAsync(false);
        PageReview.KeyDown += async (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None || e.Source is TextBox || _reviewBusy) return;
            if (e.Key == Key.Space && !_reviewRevealed) { RevealReview(); e.Handled = true; }
            else if (_reviewRevealed && e.Key is Key.Left or Key.Right)
            { e.Handled = true; await RateReviewAsync(e.Key == Key.Right); }
        };
    }

    private void OpenReviewDeck()
    {
        ++_reviewEpoch;
        RenderReviewCard();
    }

    private List<WordItem> GetPendingReviewWords()
    {
        if (_reviewDay != DateTime.Today) { _reviewHandled.Clear(); _reviewDay = DateTime.Today; }
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return _allWords.Where(w => w.Status == "learning" && w.NextReviewDate != null
            && string.CompareOrdinal(w.NextReviewDate, today) <= 0
            && (!_reviewHandled.TryGetValue(w.Id, out var handledRevision) || handledRevision != w.Archive.Revision))
            .OrderBy(w => w.NextReviewDate).ThenBy(w => w.Id).ToList();
    }

    private void UpdateReviewBadge() => NavReviewBadge.Text = GetPendingReviewWords().Count.ToString();

    private void RenderReviewCard(bool resetPose = true)
    {
        var due = GetPendingReviewWords();
        NavReviewBadge.Text = due.Count.ToString();
        _reviewSession.Reset(due);
        _reviewWord = _reviewSession.Current;
        _reviewRevealed = false;
        ReviewRemainingText.Text = $"还剩 {due.Count} 个词";
        ReviewAnswer.IsVisible = ReviewRatingBar.IsVisible = false;
        ReviewMeaningText.Text = ""; ReviewDefinitionText.Text = "";
        ReviewRevealBtn.IsVisible = _reviewWord != null;
        ReviewCardHost.IsVisible = _reviewWord != null;
        ReviewEmptyCard.IsVisible = _reviewWord == null;
        ReviewBackOne.IsVisible = due.Count > 1;
        ReviewBackTwo.IsVisible = due.Count > 2;
        ReviewWordText.Text = _reviewWord?.Word ?? "";
        ReviewPhoneticText.Text = _reviewWord?.Phonetic ?? "";
        ReviewHintText.Text = "先在心里想一想它的意思，再查看释义。";
        if (resetPose) SetReviewPose(0, 1);
        if (_reviewWord != null && _currentPage == "review") ReviewRevealBtn.Focus();
    }

    private void SetReviewPose(double x, double opacity)
    {
        if (ReviewCard.RenderTransform is not TranslateTransform) ReviewCard.RenderTransform = new TranslateTransform();
        ((TranslateTransform)ReviewCard.RenderTransform).X = x;
        ReviewCard.Opacity = opacity;
    }

    // Keep final property values rather than letting an animation clock revert to the
    // old visible card. Content is replaced only at opacity zero, then eased in.
    private async Task<bool> MoveReviewCardAsync(int epoch, double fromX, double toX, bool entering)
    {
        var duration = entering ? 190d : 150d;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (epoch != _reviewEpoch || _currentPage != "review") return false;
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration);
            var ease = entering ? 1 - Math.Pow(1 - t, 3) : t * t;
            SetReviewPose(fromX + (toX - fromX) * ease, entering ? ease : 1 - ease);
            if (t >= 1) return true;
            await Task.Delay(16);
        }
    }

    private void RevealReview()
    {
        if (_reviewWord == null || _reviewBusy || _reviewRevealed) return;
        _reviewRevealed = true;
        _reviewSession.Reveal();
        ReviewMeaningText.Text = _reviewWord.Translation;
        ReviewDefinitionText.Text = _reviewWord.Definition;
        ReviewDefinitionText.IsVisible = !string.IsNullOrWhiteSpace(_reviewWord.Definition);
        ReviewAnswer.IsVisible = ReviewRatingBar.IsVisible = true;
        ReviewRevealBtn.IsVisible = false;
        ReviewHintText.Text = "按刚才的回忆判断，不必勉强。";
        ReviewRememberBtn.Focus();
    }

    private async Task RateReviewAsync(bool remembered)
    {
        if (_reviewWord == null || !_reviewRevealed || _reviewBusy || _restoring || !_databaseAvailable) return;
        _reviewBusy = true;
        ReviewRatingBar.IsEnabled = false;
        var word = _reviewWord;
        var epoch = _reviewEpoch;
        try
        {
            if (remembered) _vocabService.ExecuteBatch([word.Id], "review");
            else _vocabService.MarkUnfamiliar(word.Id);
            _reviewSession.CompleteCurrent();
            RefreshWords();
            var updated = _allWords.FirstOrDefault(w => w.Id == word.Id);
            if (updated != null) _reviewHandled[word.Id] = updated.Archive.Revision;
            UpdateReviewBadge();
            if (!ReduceMotionBox.IsChecked.GetValueOrDefault() && _currentPage == "review" && epoch == _reviewEpoch)
            {
                var direction = remembered ? 1 : -1;
                if (!await MoveReviewCardAsync(epoch, 0, direction * 48, false)) return;
                RenderReviewCard(resetPose: false);
                SetReviewPose(-direction * 32, 0);
                if (_reviewWord != null && !await MoveReviewCardAsync(epoch, -direction * 32, 0, true)) return;
                SetReviewPose(0, 1);
            }
            else if (epoch == _reviewEpoch) RenderReviewCard();
            SetStatus(remembered ? "已记下这次重逢。" : "已安排明日再见。" );
        }
        catch (Exception ex) { SetStatus("本次复习未完成，请重试：" + ex.Message); }
        finally { _reviewBusy = false; ReviewRatingBar.IsEnabled = true; }
    }
}

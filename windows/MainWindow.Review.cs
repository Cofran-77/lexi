using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;

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
        if (_reviewDay != DateTime.Today) { _reviewHandled.Clear(); _reviewDay = DateTime.Today; }
        RenderReviewCard();
    }

    private void RenderReviewCard()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var due = _allWords.Where(w => w.Status == "learning" && w.NextReviewDate != null
            && string.CompareOrdinal(w.NextReviewDate, today) <= 0
            && (!_reviewHandled.TryGetValue(w.Id, out var handledRevision) || handledRevision != w.Archive.Revision))
            .OrderBy(w => w.NextReviewDate).ThenBy(w => w.Id).ToList();
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
        ReviewCard.Opacity = 1;
        ReviewCard.RenderTransform = new TranslateTransform();
        if (_reviewWord != null && _currentPage == "review") ReviewRevealBtn.Focus();
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
            if (!ReduceMotionBox.IsChecked.GetValueOrDefault() && _currentPage == "review" && epoch == _reviewEpoch)
            {
                var slide = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, 0d), new Setter(OpacityProperty, 1d) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, remembered ? 80d : -80d), new Setter(OpacityProperty, 0d) } }
                    }
                };
                await slide.RunAsync(ReviewCard);
            }
            SetStatus(remembered ? "已记下这次重逢。" : "已安排明日再见。" );
            if (epoch == _reviewEpoch) RenderReviewCard();
        }
        catch (Exception ex) { SetStatus("本次复习未完成，请重试：" + ex.Message); }
        finally { _reviewBusy = false; ReviewRatingBar.IsEnabled = true; }
    }
}

using System;
using System.IO;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Lexi.Social;

namespace Lexi;

public partial class MainWindow
{
    // Conservative activity clock backing the statistics page. Only surfaces that
    // are visibly in use and recently interacted with accumulate seconds.
    private StudyActivityStore? _studyActivityStore;
    private StudyActivityTracker? _studyActivityTracker;
    private DispatcherTimer? _studyActivityTimer;
    private DateTime _studyActivityLastInteractionUtc = DateTime.UtcNow;
    private DateTime _studyActivityLastFlushUtc = DateTime.MinValue;

    private string StudyActivityFilePath =>
        Path.Combine(Path.GetDirectoryName(_vocabService.DatabasePath)!, "study-activity.json");

    private void InitializeStudyActivityTracking()
    {
        _studyActivityStore = new StudyActivityStore(StudyActivityFilePath);
        _studyActivityTracker = new StudyActivityTracker();

        AddHandler(InputElement.PointerPressedEvent, (_, _) => _studyActivityLastInteractionUtc = DateTime.UtcNow,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.KeyDownEvent, (_, _) => _studyActivityLastInteractionUtc = DateTime.UtcNow,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        _studyActivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _studyActivityTimer.Tick += (_, _) => TickStudyActivity();
        _studyActivityTimer.Start();

        Closed += (_, _) => FlushStudyActivity(final: true);
    }

    private void TickStudyActivity()
    {
        if (_studyActivityTracker == null) return;
        var now = DateTime.UtcNow;
        var kind = CurrentStudyActivityKind();
        var recentlyInteracted = now - _studyActivityLastInteractionUtc <= TimeSpan.FromSeconds(30);
        _studyActivityTracker.Advance(now, kind, kind.HasValue && IsActive, recentlyInteracted);
        FlushStudyActivity(final: false);
    }

    private StudyActivityKind? CurrentStudyActivityKind()
    {
        if (_typingHost?.IsVisible == true) return StudyActivityKind.Spelling;
        if (_currentPage == "review" && _reviewWord != null) return StudyActivityKind.Review;
        if (_focusActive && _focusPresentation != null && _focusRound != null)
            return _focusRound.Mode == StudyMode.FirstLearn ? StudyActivityKind.FirstLearn : StudyActivityKind.Review;
        if (_planPresentation != null && _dailyLearningSession != null)
            return _dailyLearningSession.Mode == StudyMode.FirstLearn
                ? StudyActivityKind.FirstLearn
                : StudyActivityKind.Review;
        return null;
    }

    private void FlushStudyActivity(bool final)
    {
        if (_studyActivityStore == null || _studyActivityTracker == null) return;
        var now = DateTime.UtcNow;
        if (!final && now - _studyActivityLastFlushUtc < TimeSpan.FromSeconds(30)) return;
        var pending = _studyActivityTracker.Drain();
        _studyActivityLastFlushUtc = now;
        if (pending.Count == 0) return;
        if (!_studyActivityStore.Append(pending).Success) return;
        // 关闭窗口时只保证本地落盘；再次进入应用时会由恢复流程补观察，避免重启计时器。
        if (!final) SocialObserveSaved(QualificationCause.Correction);
    }
}

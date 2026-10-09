namespace Lexi;

public partial class MainWindow
{
    private sealed record MemoryPresentation(WordKey Key, string Id, string Identity, StudyStep Step);

    private LearningMemoryCoordinator? _learningMemory;
    private ILearningMemoryStore? _learningMemoryStore;
    private IVocabularyArchive? _learningMemoryArchive;
    private FsrsPersonalizationRuntime? _memoryParameterRuntime;
    private ContextCalibrator? _memoryContextTrainer;
    private bool _contextTrainingRunning;
    private DateTime? _contextCompletedAtUtc;
    private DateTime? _contextFailedAtUtc;
    private CrossStoreJournal? _memoryJournal;
    private MemoryPresentation? _reviewPresentation;
    private MemoryPresentation? _reviewLastPresentation;
    private bool _reviewLastCommitted;
    private MemoryPresentation? _focusPresentation;
    private MemoryPresentation? _focusLastPresentation;
    private bool _focusLastCommitted;
    private MemoryPresentation? _planPresentation;
    private MemoryPresentation? _planLastPresentation;
    private bool _planLastCommitted;
    private int _planStreakBefore;
    private bool _planRatingApplied;

    private LearningMemoryCoordinator Memory()
    {
        if (ReferenceEquals(_learningMemoryArchive, _vocabService) && _learningMemory != null)
            return _learningMemory;

        var store = ServiceFactory.OpenMemory(_vocabService);
        var journal = new CrossStoreJournal(store, Path.GetDirectoryName(store.DatabasePath)!);
        var replay = journal.Replay(DateTime.UtcNow);
        if (!replay.Succeeded) throw new IOException("待完成的学习进度尚未写回，请检查数据目录。" + replay.Error);
        if (replay.Applied > 0 && _learningHubPage != null) ReloadLearningPlans();
        var weights = new SchedulerWeights();
        var helper = Environment.GetEnvironmentVariable("LEXI_FSRS_OPTIMIZER_BIN");
        if (string.IsNullOrWhiteSpace(helper)) helper = Path.Combine(AppContext.BaseDirectory, "fsrs-optimizer.exe");
        var startup = FsrsPersonalizationStartup.Create(store, weights,
            _ => new FsrsParameterOptimizer(new FsrsOptimizerOptions { HelperPath = helper }));
        if (!startup.Consistent)
            throw new InvalidOperationException("长期记忆参数与已有卡片不一致，已暂停写入以保护学习数据。" + startup.Error);
        var scheduler = new Fsrs6Scheduler(weights);
        _memoryParameterRuntime?.Invalidate();
        _memoryParameterRuntime = new FsrsPersonalizationRuntime(startup.Service,
            deferPublish: () => _restoring || !_databaseAvailable || _focusActive || _currentPage == "review",
            diagnostic: message => Console.Error.WriteLine("[memory] " + message));
        _memoryContextTrainer = new ContextCalibrator(store, scheduler);
        _learningMemory = new LearningMemoryCoordinator(store, scheduler,
            new ContextFeatureProvider(store, scheduler), _memoryContextTrainer);
        _learningMemoryStore = store;
        _memoryJournal = journal;
        _learningMemoryArchive = _vocabService;
        _reviewPresentation = _reviewLastPresentation = null;
        _focusPresentation = _focusLastPresentation = null;
        _planPresentation = _planLastPresentation = null;
        return _learningMemory;
    }

    private void BeginReviewMemory(int count)
    {
        var memory = Memory();
        TryTrainContext("review-start");
        memory.BeginSession(StudyMode.Review, WordSource.Archive, "", count);
        _reviewPresentation = _reviewLastPresentation = null;
        _reviewLastCommitted = false;
    }

    private void PresentReviewMemory(WordItem? word)
    {
        if (word == null || _reviewPresentation?.Identity == word.Id.ToString()) return;
        var key = WordKeyResolver.FromArchive(word);
        var presented = new MemoryPresentation(key, Guid.NewGuid().ToString("N"), word.Id.ToString(), StudyStep.Recall);
        Memory().OnPresented(key, presented.Id, isRecall: true);
        _reviewPresentation = presented;
    }

    private void RateReviewMemory(StudyRating rating, int before, int after, bool completed)
    {
        var presented = _reviewPresentation ?? throw new InvalidOperationException("复习卡没有对应的呈现记录。");
        var memory = Memory();
        memory.OnRated(presented.Key, presented.Id, rating, before, after, null);
        _reviewLastPresentation = presented;
        _reviewLastCommitted = false;
        try
        {
            if (completed)
            {
                _ = memory.CommitWord(presented.Key, StudyMode.Review)
                    ?? throw new InvalidOperationException("复习轨迹未能生成长期排期。");
                _reviewLastCommitted = true;
                if (_reviewRound.IsFinished) NotifyMemoryRoundBoundary("review-completed");
            }
        }
        catch { UndoReviewMemory(); throw; }
        _reviewPresentation = null;
    }

    private void UndoReviewMemory()
    {
        var presented = _reviewLastPresentation ?? throw new InvalidOperationException("没有可撤销的复习轨迹。");
        var memory = Memory();
        memory.OnUndone(presented.Key, presented.Id, canonicalId: _reviewLastCommitted
            ? CanonicalReview.BuildId(presented.Key.Key, memory.CurrentSessionId) : null);
        _reviewPresentation = presented;
        _reviewLastPresentation = null;
        _reviewLastCommitted = false;
    }

    private HashSet<long> DueArchiveIds()
    {
        var memory = Memory();
        var store = _learningMemoryStore as VocabularyService;
        if (store == null) throw new InvalidOperationException("长期复习词库未绑定。");
        var cardKeys = store.GetMemoryCardKeys();
        var dueKeys = memory.QueryDue(DateTime.UtcNow, int.MaxValue)
            .Select(card => card.WordKey).ToHashSet(StringComparer.Ordinal);
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return _allWords.Where(word => word.Status == "learning" &&
            (dueKeys.Contains(WordKeyResolver.FromArchive(word).Key) ||
             (!cardKeys.Contains(WordKeyResolver.FromArchive(word).Key) && word.NextReviewDate != null &&
              string.CompareOrdinal(word.NextReviewDate, today) <= 0)))
            .Select(word => word.Id).ToHashSet();
    }

    private void BeginFocusMemory(StudyMode mode, int count)
    {
        Memory().BeginSession(mode, WordSource.Archive, "", count);
        _focusPresentation = _focusLastPresentation = null;
        _focusLastCommitted = false;
    }

    private void PresentFocusMemory(string word, StudyStep step)
    {
        var identity = WordKeyResolver.FormC(word);
        if (_focusPresentation is { } current && current.Identity == identity && current.Step == step) return;
        var archive = _allWords.FirstOrDefault(item => WordKeyResolver.FormC(item.Word) == identity);
        var key = archive == null ? WordKeyResolver.FromForm(word) : WordKeyResolver.FromArchive(archive);
        var presented = new MemoryPresentation(key, Guid.NewGuid().ToString("N"), identity, step);
        Memory().OnPresented(key, presented.Id, step == StudyStep.Recall);
        _focusPresentation = presented;
    }

    private void RateFocusMemory(StudyRating rating, int before, int after, bool completed, StudyMode mode)
    {
        var presented = _focusPresentation ?? throw new InvalidOperationException("专注卡没有对应的呈现记录。");
        var memory = Memory();
        memory.OnRated(presented.Key, presented.Id, rating, before, after, null);
        _focusLastPresentation = presented;
        _focusLastCommitted = false;
        try
        {
            if (completed)
            {
                _ = memory.CommitWord(presented.Key, mode)
                    ?? throw new InvalidOperationException("专注轨迹未能生成长期排期。");
                _focusLastCommitted = true;
                if (_focusRound?.IsFinished == true) NotifyMemoryRoundBoundary("focus-completed");
            }
        }
        catch { UndoFocusMemory(); throw; }
        _focusPresentation = null;
    }

    private void UndoFocusMemory()
    {
        var presented = _focusLastPresentation ?? throw new InvalidOperationException("没有可撤销的专注轨迹。");
        var memory = Memory();
        memory.OnUndone(presented.Key, presented.Id, canonicalId: _focusLastCommitted
            ? CanonicalReview.BuildId(presented.Key.Key, memory.CurrentSessionId) : null);
        _focusPresentation = presented;
        _focusLastPresentation = null;
        _focusLastCommitted = false;
    }

    private void BeginPlanMemory(DailyStudyPlan plan, DailyStudyPlanSession session)
    {
        var source = plan.Source == DailyStudyPlanSource.Ielts ? WordSource.Ielts : WordSource.Archive;
        TryTrainContext("plan-start");
        Memory().BeginSession(StudyMode.FirstLearn, source, plan.Id, session.Round.Total);
        _planPresentation = _planLastPresentation = null;
        _planLastCommitted = false;
        session.OnRatingApplied = (_, rating, result) => RatePlanMemory(rating, result);
        session.OnWordCompleted = CommitPlanMemory;
        session.OnRatingFailed = _ => { if (_planRatingApplied) UndoPlanMemory(); };
    }

    private void PresentPlanMemory(DailyStudyPlan plan, string wordId, StudyStep step)
    {
        if (_planPresentation is { } current && current.Identity == wordId && current.Step == step) return;
        var word = plan.Words.Single(w => w.Id == wordId);
        var key = WordKeyResolver.ResolvePlanWord(plan, word,
            id => _allWords.FirstOrDefault(item => item.Id.ToString() == id));
        var presented = new MemoryPresentation(key, Guid.NewGuid().ToString("N"), wordId, step);
        Memory().OnPresented(key, presented.Id, step == StudyStep.Recall);
        _planPresentation = presented;
    }

    private void RatePlanMemory(StudyRating rating, StudyCommitResult result)
    {
        var presented = _planPresentation ?? throw new InvalidOperationException("计划卡没有对应的呈现记录。");
        Memory().OnRated(presented.Key, presented.Id, rating, _planStreakBefore, result.Streak, null);
        _planRatingApplied = true;
        _planLastPresentation = presented;
        _planLastCommitted = false;
        _planPresentation = null;
    }

    private void CommitPlanMemory(string wordId)
    {
        var presented = _planLastPresentation;
        if (presented == null || presented.Identity != wordId)
            throw new InvalidOperationException("计划词与最后一次作答不一致。");
        var mutation = CrossStoreJournal.CreateJsonSnapshot("daily-plans.json",
            System.Text.Json.JsonSerializer.Serialize(_learningPlans));
        _ = Memory().CommitWord(presented.Key, StudyMode.FirstLearn, outboxMutations: [mutation])
            ?? throw new InvalidOperationException("计划词尚无有效回忆，无法完成。");
        _planLastCommitted = true;
        if (_dailyLearningSession?.Round.IsFinished == true) NotifyMemoryRoundBoundary("plan-completed");
    }

    private void UndoPlanMemory()
    {
        var presented = _planLastPresentation;
        if (presented == null) return;
        var memory = Memory();
        memory.OnUndone(presented.Key, presented.Id, canonicalId: _planLastCommitted
            ? CanonicalReview.BuildId(presented.Key.Key, memory.CurrentSessionId) : null);
        _planPresentation = presented;
        _planLastPresentation = null;
        _planLastCommitted = false;
        _planRatingApplied = false;
    }

    private bool ReplayMemoryJournal()
    {
        if (_memoryJournal == null) return true;
        var result = _memoryJournal.Replay(DateTime.UtcNow);
        if (result.Succeeded) return true;
        SetStatus("学习进度正在等待写回：" + result.Error);
        return false;
    }

    private void NotifyMemoryRoundBoundary(string trigger)
    {
        _memoryParameterRuntime?.NotifyRoundBoundary(trigger);
        TryTrainContext(trigger);
    }

    internal static bool ShouldTrainContext(DateTime nowUtc, DateTime? lastCompletedAtUtc,
        DateTime? lastFailedAtUtc, bool hasPendingTask, int evaluableSampleCount)
    {
        if (hasPendingTask || evaluableSampleCount < SchedulingConfig.ContextMinimumSamples) return false;
        var cooldown = TimeSpan.FromMinutes(SchedulingConfig.ContextTrainingCooldownMinutes);
        return !(lastCompletedAtUtc is { } completed && nowUtc - completed < cooldown)
            && !(lastFailedAtUtc is { } failed && nowUtc - failed < cooldown);
    }

    private async void TryTrainContext(string trigger)
    {
        var trainer = _memoryContextTrainer;
        var store = _learningMemoryStore;
        if (trainer == null || store == null) return;
        var now = DateTime.UtcNow;
        if (!ShouldTrainContext(now, _contextCompletedAtUtc, _contextFailedAtUtc,
                _contextTrainingRunning, SchedulingConfig.ContextMinimumSamples)) return;
        int count;
        try { count = store.LoadLabeledSamples().Count; }
        catch (Exception ex) { Console.Error.WriteLine("[memory] context samples: " + ex.Message); return; }
        if (!ShouldTrainContext(now, _contextCompletedAtUtc, _contextFailedAtUtc, _contextTrainingRunning, count)) return;
        _contextTrainingRunning = true;
        try
        {
            await trainer.TrainAsync(now);
            _contextCompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _contextFailedAtUtc = DateTime.UtcNow;
            Console.Error.WriteLine("[memory] " + trigger + " context training: " + ex.Message);
        }
        finally { _contextTrainingRunning = false; }
    }
}

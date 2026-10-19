using System.Text.Json;

namespace Lexi;

public partial class MainWindow
{
    private sealed record SurfaceCheckpoint(int Version, string Scope, string Round,
        MemoryPresentation? Current, MemoryPresentation? Last, bool LastCommitted,
        bool Revealed, bool Rated, StudyRating LastRating, string? RatedWord,
        string PreviousPage, string Translation, string? PendingKey = null,
        StudyMode PendingMode = StudyMode.FirstLearn, string? PendingIdentity = null,
        string? PlanJson = null, string? PlanUndo = null,
        long UndoWord = 0, bool UndoPersisted = false, int UndoRevision = -1,
        long? FocusUndoArchiveId = null, int? FocusUndoRevision = null, bool FocusUndoIsRating = true,
        Dictionary<long, int>? ReviewVersions = null, string? PlanId = null,
        string? LearningDate = null, StudyMode Mode = StudyMode.FirstLearn);

    private MemorySessionCheckpoint? CaptureSurfaceCheckpoint(string surface, WordKey? pending = null,
        StudyMode pendingMode = StudyMode.Review, string? pendingIdentity = null)
    {
        LearningMemoryCoordinator? memory;
        SurfaceCheckpoint saved;
        if (surface == "review")
        {
            memory = _reviewMemory;
            saved = new(1, "review", _reviewRound.CaptureJson(id => id.ToString()),
                _reviewPresentation, _reviewLastPresentation, _reviewLastCommitted,
                _reviewRevealed, false, StudyRating.Known, null, "review", "",
                ReviewVersions: new Dictionary<long, int>(_reviewVersions));
        }
        else if (surface == "focus" && _focusRound != null)
        {
            memory = _focusMemory;
            var scope = FocusSurfaceScope();
            saved = new(1, scope, _focusRound.CaptureJson(w => w),
                _focusPresentation, _focusLastPresentation, _focusLastCommitted,
                _focusAnswerVisible, _focusRated, _focusLastRating, _focusRatedWord,
                _focusPreviousPage, _focusOriginalTranslation);
        }
        else if (surface == "plan" && _dailyLearningSession != null && _activeLearningPlan != null)
        {
            memory = _planMemory;
            saved = new(2, PlanSurfaceScope(_activeLearningPlan, _dailyLearningSession.Mode),
                _dailyLearningSession.Round.CaptureJson(w => w),
                _planPresentation, _planLastPresentation, _planLastCommitted,
                _planAnswerVisible, _planRatedPending, StudyRating.Known, _planRatedWordId,
                "learning", "",
                PlanId: _activeLearningPlan.Id, LearningDate: LearningDateText(_dailyLearningDate),
                Mode: _dailyLearningSession.Mode);
        }
        else return null;
        saved = saved with { PendingKey = pending?.Key, PendingMode = pendingMode,
            PendingIdentity = pendingIdentity,
            PlanJson = surface == "plan" ? JsonSerializer.Serialize(_activeLearningPlan) : null,
            PlanUndo = surface == "plan" ? _dailyLearningSession?.CaptureUndoJson() : null,
            UndoWord = _reviewUndoWord, UndoPersisted = _reviewUndoPersisted, UndoRevision = _reviewUndoRevision,
            FocusUndoArchiveId = _focusUndoArchiveId, FocusUndoRevision = _focusUndoRevision,
            FocusUndoIsRating = _focusUndoIsRating };
        if (memory == null || string.IsNullOrWhiteSpace(memory.CurrentSessionId)) return null;
        var storageSurface = surface == "plan"
            ? PlanCheckpointSurface(_activeLearningPlan!, _dailyLearningDate, _dailyLearningSession!.Mode)
            : surface;
        return new(storageSurface, memory.CurrentSessionId, JsonSerializer.Serialize(saved), DateTime.UtcNow);
    }

    private void PersistLearningSurface(string surface)
    {
        if (_learningMemoryStore == null) return;
        var checkpoint = CaptureSurfaceCheckpoint(surface);
        if (checkpoint != null) _learningMemoryStore.SaveSessionCheckpoint(checkpoint);
    }

    private void PersistPausedSurfaces()
    {
        foreach (var surface in new[] { "review", "focus", "plan" })
            try { PersistLearningSurface(surface); }
            catch (Exception ex) { SetStatus("学习断点保存失败，当前内存进度保留：" + ex.Message); }
    }

    private bool RestoreLearningSurface(string surface, string scope, LearningMemoryCoordinator memory)
    {
        var storageSurface = surface == "plan" && _activeLearningPlan is not null &&
            _dailyLearningSession is not null
            ? PlanCheckpointSurface(_activeLearningPlan, _dailyLearningDate, _dailyLearningSession.Mode)
            : surface;
        var checkpoint = _learningMemoryStore?.GetSessionCheckpoint(storageSurface);
        if (checkpoint == null) return false;
        var saved = JsonSerializer.Deserialize<SurfaceCheckpoint>(checkpoint.QueueJson)
            ?? throw new InvalidDataException("学习断点内容为空。");
        var expectedVersion = surface == "plan" ? 2 : 1;
        if (saved.Version != expectedVersion)
            throw new InvalidDataException("学习断点版本无法恢复，已保留原记录。");
        if (saved.Scope != scope) return false;
        JsonElement planRoundRoot = default;
        if (surface == "plan" &&
            !PlanCheckpointMatches(saved, checkpoint, storageSurface, out planRoundRoot))
        {
            _learningMemoryStore!.DeleteSessionCheckpoint(checkpoint.Surface, checkpoint.SessionId);
            return false;
        }
        if (surface == "review" && saved.ReviewVersions is { Count: > 0 } versions &&
            (_allWords.Count != versions.Count || _allWords.Any(w =>
                !versions.TryGetValue(w.Id, out var revision) || revision != w.Archive.Revision)))
        {
            _learningMemoryStore!.DeleteSessionCheckpoint(checkpoint.Surface, checkpoint.SessionId);
            return false;
        }
        using var roundDocument = surface == "plan" ? null : JsonDocument.Parse(saved.Round);
        var roundRoot = surface == "plan" ? planRoundRoot : roundDocument!.RootElement;
        var finished = roundRoot.GetProperty("Completed").GetInt32() == roundRoot.GetProperty("Total").GetInt32();
        WordKey? pendingWord = null;
        DailyStudyPlan? planProgress = null;
        if (saved.PendingKey is not null)
        {
            if (surface == "plan")
            {
                if (!TryReadPendingPlanCommit(saved, checkpoint, roundRoot, out planProgress, out var validatedWord))
                {
                    _learningMemoryStore!.DeleteSessionCheckpoint(checkpoint.Surface, checkpoint.SessionId);
                    return false;
                }
                pendingWord = validatedWord;
            }
            else
            {
                try { pendingWord = WordKey.Parse(saved.PendingKey); }
                catch (Exception ex) when (ex is ArgumentException or FormatException)
                {
                    _learningMemoryStore!.DeleteSessionCheckpoint(checkpoint.Surface, checkpoint.SessionId);
                    return false;
                }
            }
        }
        if (!memory.ResumeSession(checkpoint.SessionId)) return false;
        if (pendingWord is { } pending)
        {
            if (surface == "plan" && planProgress is { } progress)
            {
                _activeLearningPlan!.CompletedWordIds = progress.CompletedWordIds;
                _activeLearningPlan.ForgotWordIds = progress.ForgotWordIds;
                _activeLearningPlan.Status = progress.Status;
                _activeLearningPlan.LastBatchCompletedDate = progress.LastBatchCompletedDate;
                _activeLearningPlan.LastReviewCompletedDate = progress.LastReviewCompletedDate;
                var mutation = CrossStoreJournal.CreateJsonSnapshot("daily-plans.json", JsonSerializer.Serialize(_learningPlans));
                memory.CommitWord(pending, saved.PendingMode, outboxMutations: [mutation]);
                if (!ReplayMemoryJournal()) throw new IOException("计划断点等待进度写回。");
            }
            else memory.CommitWord(pending, saved.PendingMode);
            saved = saved with { PendingKey = null, PendingIdentity = null, LastCommitted = true };
            _learningMemoryStore!.SaveSessionCheckpoint(checkpoint with { QueueJson = JsonSerializer.Serialize(saved) });
        }
        if (finished && !(surface == "plan" && saved.Rated))
        {
            _learningMemoryStore!.DeleteSessionCheckpoint(checkpoint.Surface, checkpoint.SessionId);
            return false;
        }
        if (surface == "review")
        {
            SourceDueWords();
            _reviewRound.RestoreJson(saved.Round, id =>
            {
                if (!long.TryParse(id, out var numeric) || ResolveReviewWord(numeric) == null)
                    throw new InvalidDataException("断点词条已不存在：" + id);
                return numeric;
            });
            _reviewPresentation = saved.Current; _reviewLastPresentation = saved.Last;
            _reviewLastCommitted = saved.LastCommitted; _reviewRevealed = saved.Revealed;
            _reviewRestoreRevealed = saved.Revealed;
            if (_reviewRound.CanUndo && saved.Last != null)
            {
                var after = _reviewRound.CaptureCheckpoint();
                var undo = _reviewRound.UndoLast()!.Value;
                _reviewUndo = _reviewRound.CaptureCheckpoint(); after.Restore();
                _reviewUndoWord = undo.Word; _reviewUndoPersisted = saved.UndoPersisted;
                _reviewUndoRevision = saved.UndoRevision;
            }
        }
        else if (surface == "focus")
        {
            _focusRound!.RestoreJson(saved.Round, id => id);
            _focusPresentation = saved.Current; _focusLastPresentation = saved.Last;
            _focusLastCommitted = saved.LastCommitted; _focusAnswerVisible = saved.Revealed;
            _focusRated = saved.Rated; _focusLastRating = saved.LastRating;
            _focusRatedWord = saved.RatedWord;
            _focusPreviousPage = NormalizeFocusReturnPage(saved.PreviousPage);
            _focusOriginalTranslation = saved.Translation;
            if (_focusRound.CanUndo)
            {
                var after = _focusRound.CaptureCheckpoint(); _focusRound.UndoLast();
                _focusUndo = _focusRound.CaptureCheckpoint(); after.Restore();
                _focusUndoArchiveId = saved.FocusUndoArchiveId; _focusUndoRevision = saved.FocusUndoRevision;
                _focusUndoIsRating = saved.FocusUndoIsRating;
            }
        }
        else
        {
            _dailyLearningSession!.Round.RestoreJson(saved.Round, id =>
                _activeLearningPlan!.Words.Single(w => w.Id == id).Id);
            _planPresentation = saved.Current; _planLastPresentation = saved.Last;
            _planLastCommitted = saved.LastCommitted; _planAnswerVisible = saved.Revealed;
            _planRatedPending = saved.Rated &&
                !string.IsNullOrWhiteSpace(saved.RatedWord) &&
                _activeLearningPlan!.Words.Any(word => word.Id == saved.RatedWord);
            _planRatedWordId = _planRatedPending ? saved.RatedWord : null;
            _planRatedWordCompleted = _planRatedPending &&
                _activeLearningPlan!.CompletedWordIds.Contains(saved.RatedWord!);
            if (_planRatedPending) _planAnswerVisible = true;
            _dailyLearningSession.RestoreUndoJson(saved.PlanUndo);
        }
        return true;
    }

    private bool PlanCheckpointMatches(SurfaceCheckpoint saved, MemorySessionCheckpoint checkpoint,
        string expectedSurface, out JsonElement roundRoot)
    {
        roundRoot = default;
        var plan = _activeLearningPlan;
        var session = _dailyLearningSession;
        if (plan is null || session is null || string.IsNullOrWhiteSpace(saved.PlanId) ||
            saved.PlanId != plan.Id || saved.LearningDate != LearningDateText(_dailyLearningDate) ||
            saved.Mode != session.Mode || checkpoint.Surface != expectedSurface ||
            saved.Scope != PlanSurfaceScope(plan, session.Mode))
            return false;
        var stored = _learningMemoryStore?.GetSession(checkpoint.SessionId);
        var expectedSource = plan.Source == DailyStudyPlanSource.Ielts ? WordSource.Ielts : WordSource.Archive;
        if (stored is null || stored.EndedAtUtc is not null || stored.Mode != session.Mode ||
            stored.PlanId != plan.Id || stored.PrimarySource != expectedSource)
            return false;
        try
        {
            using var document = JsonDocument.Parse(saved.Round);
            roundRoot = document.RootElement.Clone();
            return roundRoot.TryGetProperty("Mode", out var mode) &&
                mode.ValueKind == JsonValueKind.Number && mode.GetInt32() == (int)session.Mode;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryReadPendingPlanCommit(SurfaceCheckpoint saved,
        MemorySessionCheckpoint checkpoint, JsonElement roundRoot,
        out DailyStudyPlan progress, out WordKey pendingWord)
    {
        progress = null!;
        pendingWord = default;
        var plan = _activeLearningPlan;
        if (plan is null || checkpoint.Surface != PlanCheckpointSurface(plan, _dailyLearningDate,
                saved.Mode) ||
            saved.Scope != PlanSurfaceScope(plan, saved.PendingMode) ||
            saved.Mode != saved.PendingMode ||
            saved.PendingMode is not (StudyMode.FirstLearn or StudyMode.Review) || saved.PlanJson is null)
            return false;
        var session = _learningMemoryStore?.GetSession(checkpoint.SessionId);
        var expectedSource = plan.Source == DailyStudyPlanSource.Ielts ? WordSource.Ielts : WordSource.Archive;
        if (session is null || session.EndedAtUtc is not null || session.Mode != saved.PendingMode ||
            session.PlanId != plan.Id || session.PrimarySource != expectedSource)
            return false;
        try
        {
            progress = JsonSerializer.Deserialize<DailyStudyPlan>(saved.PlanJson)!;
            var parsedPendingWord = WordKey.Parse(saved.PendingKey!);
            if (progress is null || progress.Id != plan.Id ||
                progress.CompletedWordIds is null || progress.ForgotWordIds is null ||
                progress.CompletedWordIds.Any(id => plan.Words.All(word => word.Id != id)) ||
                progress.ForgotWordIds.Any(id => plan.Words.All(word => word.Id != id)))
                return false;
            var identity = saved.PendingIdentity;
            if (string.IsNullOrWhiteSpace(identity))
            {
                var matches = plan.Words.Where(word =>
                    WordKeyResolver.ResolvePlanWord(plan, word,
                        id => _allWords.FirstOrDefault(item => item.Id.ToString() == id)) == parsedPendingWord).ToList();
                if (matches.Count != 1) return false;
                identity = matches[0].Id;
            }
            var pendingPlanWord = plan.Words.SingleOrDefault(word => word.Id == identity);
            if (pendingPlanWord is null ||
                WordKeyResolver.ResolvePlanWord(plan, pendingPlanWord,
                    id => _allWords.FirstOrDefault(item => item.Id.ToString() == id)) != parsedPendingWord ||
                (saved.PendingMode == StudyMode.FirstLearn && !progress.CompletedWordIds.Contains(identity)))
                return false;
            if (!roundRoot.TryGetProperty("Mode", out var mode) || mode.ValueKind != JsonValueKind.Number)
                return false;
            var roundMode = mode.GetInt32();
            if (roundMode != (int)saved.PendingMode ||
                !roundRoot.TryGetProperty("States", out var states) || states.ValueKind != JsonValueKind.Array ||
                !states.EnumerateArray().Any(state =>
                    state.TryGetProperty("WordId", out var id) &&
                    id.ValueKind == JsonValueKind.String && id.GetString() == identity))
                return false;
            pendingWord = parsedPendingWord;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException
            or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private static string LearningDateText(DateOnly date) => date.ToString("yyyy-MM-dd");

    internal static string PlanCheckpointSurface(DailyStudyPlan plan, DateOnly date, StudyMode mode) =>
        DailyStudyPlanRules.CheckpointSurface(plan, date, mode);
}

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lexi;

public partial class MainWindow
{
    private readonly Dictionary<long, (WordKey Key, WordItem Word)> _sourceReviewWords = [];

    // 考试词书目录（约 55 MB）只在首次需要时加载；失败不阻塞本地学习，30 秒后自动重试。
    private ExamCatalog? _examCatalog;
    private DateTime _examCatalogRetryAfterUtc = DateTime.MinValue;
    private bool _examCatalogFailureReported;
    private bool _ieltsCatalogFailureReported;

    internal ExamCatalog? EnsureExamCatalog()
    {
        if (_examCatalog != null) return _examCatalog;
        if (DateTime.UtcNow < _examCatalogRetryAfterUtc) return null;
        var failed = false;
        var catalog = ExamCatalog.TryLoad(onError: message =>
        {
            failed = true;
            if (_examCatalogFailureReported) return;
            _examCatalogFailureReported = true;
            ReportCatalogFallback("考试词书目录暂时不可用，本地学习不受影响。",
                "Exam catalog is unavailable; local learning is unaffected. ", message);
        });
        if (failed) _examCatalogRetryAfterUtc = DateTime.UtcNow.AddSeconds(30);
        return _examCatalog = catalog;
    }

    private IeltsCatalog? TryEnsureIeltsCatalog()
    {
        if (_ieltsCatalog != null) return _ieltsCatalog;
        try { return _ieltsCatalog = IeltsCatalog.Load(); }
        catch (Exception ex)
        {
            if (_ieltsCatalogFailureReported) return null;
            _ieltsCatalogFailureReported = true;
            ReportCatalogFallback("IELTS 目录暂时不可用，本地学习不受影响。",
                "The IELTS catalog is unavailable; local learning is unaffected. ", ex.Message);
            return null;
        }
    }

    private void ReportCatalogFallback(string chinese, string english, string detail)
    {
        try
        {
            if (GlobalStatusText is not null)
                SetStatus(UiText.Bilingual(chinese, english) + detail);
        }
        catch
        {
            // 目录诊断绝不能影响学习链路。
        }
    }

    private WordKey ReviewKey(WordItem word) => word.Id < 0
        && _sourceReviewWords.TryGetValue(word.Id, out var entry)
        ? entry.Key : WordKeyResolver.FromArchive(word);
    private WordItem? ResolveReviewWord(long id) => id >= 0
        ? _allWords.FirstOrDefault(w => w.Id == id)
        : _sourceReviewWords.GetValueOrDefault(id).Word;

    /// <summary>来源卡身份哈希：负数 id 与档案正 id 分离，且跨进程稳定。</summary>
    private static long SourceReviewId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return -(BinaryPrimitives.ReadInt64BigEndian(hash) & long.MaxValue | 1L);
    }

    private List<WordItem> SourceDueWords()
    {
        var pending = _activeLearningPlan is { } plan && _dailyLearningSession is { Round.IsFinished: false }
            ? plan.Words.Where(w => !plan.CompletedWordIds.Contains(w.Id))
                .Select(w => WordKeyResolver.ResolvePlanWord(plan, w,
                    id => _allWords.FirstOrDefault(a => a.Id.ToString() == id)).Key)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var result = new List<WordItem>();
        var due = Memory().QueryDue(DateTime.UtcNow, int.MaxValue).Select(c => c.WordKey).ToHashSet(StringComparer.Ordinal);
        var store = (VocabularyService)_learningMemoryStore!;
        foreach (var card in store.GetMemoryCards().Where(c => !c.WordKey.StartsWith(WordKey.ArchivePrefix, StringComparison.Ordinal)))
        {
            var key = WordKey.Parse(card.WordKey);
            var id = SourceReviewId(card.WordKey);
            if (_sourceReviewWords.TryGetValue(id, out var existing) && existing.Key != key)
                throw new InvalidDataException("教材复习身份冲突，已暂停装填。");
            // 复用同一实例：轮内/撤销持有的引用不能在每次装填时被替换。
            var word = existing.Word ?? BuildSourceReviewWord(key, id, store);
            // 早期装填可能只拿到回退文案；目录或详情就绪后每次取用都重新补全，
            // 复习卡才不会一直显示 "exam:CET4_2:1" 这类内部标识。
            if (word != null) ApplySourceReviewContent(key, word);
            if (word == null) continue; // 解析不出可显示内容时宁可不展示，也绝不把内部 ID 当单词。
            word.NextReviewDate = card.NextReviewAtUtc?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _sourceReviewWords[id] = (key, word);
            if (due.Contains(card.WordKey) && !pending.Contains(card.WordKey)) result.Add(word);
        }
        return result;
    }

    private WordItem? BuildSourceReviewWord(WordKey key, long id, VocabularyService store)
    {
        var details = store.LoadSourceWordDetails(key.Key);
        if (details == null && key.Source == WordSource.Form)
        {
            var archive = _allWords.FirstOrDefault(w =>
                string.Equals(WordKeyResolver.FormC(w.Word), key.SourceId, StringComparison.Ordinal));
            if (archive != null) details = [archive.Word, archive.Phonetic, archive.Translation, archive.Definition];
        }
        if (details == null && key.Source != WordSource.Form)
        {
            var planWord = FindPlanWord(key.SourceId);
            if (planWord != null) details = [planWord.Word, planWord.Phonetic, planWord.Meaning, planWord.Example];
        }
        LookupResult? lookup = null;
        if (details == null && !ExamCatalog.IsExamWordId(key.SourceId) && _dictService is DictionaryService dictionary)
            lookup = dictionary.Lookup(key.SourceId);
        var wordText = details?[0];
        if (string.IsNullOrWhiteSpace(wordText)) wordText = lookup?.Word;
        if (string.IsNullOrWhiteSpace(wordText)) return null;

        return new WordItem
        {
            Id = id,
            Word = wordText,
            Phonetic = details?[1] ?? lookup?.Phonetic ?? "",
            Translation = details?[2] ?? lookup?.Translation ?? "",
            Definition = details?[3] ?? lookup?.Definition ?? "",
            Status = "learning"
        };
    }

    private DailyStudyPlanWord? FindPlanWord(string sourceId)
    {
        foreach (var plan in _learningPlans)
        {
            var word = plan.Words.FirstOrDefault(w => string.Equals(w.Id, sourceId, StringComparison.Ordinal));
            if (word != null && !string.IsNullOrWhiteSpace(word.Word)) return word;
        }
        return null;
    }

    /// <summary>
    /// 把来源词身份解析成可显示内容。目录尚未加载/加载失败时保留已有回退文案，
    /// 之后同一词条可重复调用补全。
    /// </summary>
    private void ApplySourceReviewContent(WordKey key, WordItem item)
    {
        if (key.Source != WordSource.Ielts) return;
        LearningWord? entry;
        if (ExamCatalog.IsExamWordId(key.SourceId))
        {
            entry = EnsureExamCatalog()?.FindWord(key.SourceId);
            if (entry == null) return;
        }
        else
        {
            entry = TryEnsureIeltsCatalog()?.AllWords.FirstOrDefault(w => string.Equals(w.Id, key.SourceId, StringComparison.Ordinal));
        }
        if (entry == null) return;
        item.Word = entry.Word;
        item.Phonetic = entry.Phonetic;
        item.Translation = entry.Meaning;
        item.Definition = entry.Example;
        item.Notes = entry.Extra;
    }
}

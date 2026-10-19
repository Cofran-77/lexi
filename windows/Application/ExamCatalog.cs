using System.Text.Json;

namespace Lexi;

public sealed class ExamBook
{
    public string TopicId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string EnglishTitle { get; set; } = "";
    public string BookId { get; set; } = "";
    public List<LearningSection> Sections { get; set; } = [];
    public IEnumerable<LearningWord> AllWords => Sections.SelectMany(s => s.Entries);
}

/// <summary>
/// 六类考试 20 本词书的只读目录。词条身份固定为 <c>exam:&lt;BookId&gt;:&lt;n&gt;</c>，
/// 同一拼写出现在不同书中时仍是不同身份，绝不合并。
/// </summary>
public sealed class ExamCatalog
{
    public const int ExpectedBookCount = 20;
    public const int ExpectedWordCount = 66974;
    public const int MaxEntriesPerSection = 100;

    public static readonly string[] TopicIds = ["cet4", "cet6", "kaoyan", "gmat", "toefl", "gre"];

    // 与 mac 仓库 manifest.json（commit 3992bcb）一致；计数不符说明资源被截断或改写，必须失败关闭。
    private static readonly Dictionary<string, int> ExpectedBookCounts = new(StringComparer.Ordinal)
    {
        ["CET4_2"] = 3739,
        ["CET6_2"] = 2078,
        ["KaoYan_2"] = 4533,
        ["GMAT_2"] = 3254,
        ["TOEFL_2"] = 9212,
        ["GRE_2"] = 7199,
        ["CET4luan_1"] = 1162,
        ["CET6luan_1"] = 1228,
        ["KaoYanluan_1"] = 1340,
        ["CET4luan_2"] = 3739,
        ["CET4_3"] = 2607,
        ["CET6_3"] = 2345,
        ["KaoYan_3"] = 3728,
        ["CET4_1"] = 1162,
        ["CET6_1"] = 1228,
        ["KaoYan_1"] = 1340,
        ["GMATluan_2"] = 3254,
        ["TOEFL_3"] = 4264,
        ["GRE_3"] = 6515,
        ["GMAT_3"] = 3047,
    };

    private static readonly Dictionary<string, int> ExpectedTopicBookCounts = new(StringComparer.Ordinal)
    {
        ["cet4"] = 5, ["cet6"] = 4, ["kaoyan"] = 4, ["gmat"] = 3, ["toefl"] = 2, ["gre"] = 2,
    };

    public string Source { get; set; } = "";
    public string Commit { get; set; } = "";
    public List<ExamBook> Books { get; set; } = [];

    private Dictionary<string, LearningWord>? _wordIndex;
    private Dictionary<string, ExamBook>? _bookIndex;
    private Dictionary<string, ExamBook>? _bookIdIndex;
    private Dictionary<string, ExamBook>? _sectionIndex;

    private static readonly object CacheGate = new();
    private static ExamCatalog? _cached;

    public static string AssetDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Exams");

    public static ExamCatalog? TryLoad(string? folder = null, Action<string>? onError = null)
    {
        try { return Load(folder); }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
            return null;
        }
    }

    /// <summary>读取并严格校验目录。传 folder 时绕过进程缓存（用于测试/隔离数据目录）。</summary>
    public static ExamCatalog Load(string? folder = null)
    {
        if (folder == null)
        {
            lock (CacheGate)
                if (_cached != null) return _cached;
        }

        var path = Path.Combine(folder ?? AssetDirectory, "catalog.json");
        var catalog = JsonSerializer.Deserialize<ExamCatalog>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("考试词书目录为空。");
        catalog.Validate();
        catalog.BuildIndexes();
        if (folder == null)
        {
            lock (CacheGate) _cached = catalog;
        }
        return catalog;
    }

    public static void ResetCache()
    {
        lock (CacheGate) _cached = null;
    }

    private void Validate()
    {
        if (Books.Count != ExpectedBookCount)
            throw new InvalidDataException($"考试词书数量应为 {ExpectedBookCount}，实际 {Books.Count}。");
        if (!Books.Take(TopicIds.Length).Select(b => b.Id).SequenceEqual(TopicIds, StringComparer.Ordinal))
            throw new InvalidDataException("考试词书前六项必须依次为 cet4/cet6/kaoyan/gmat/toefl/gre。");
        if (Books.Any(b => !TopicIds.Contains(b.TopicId, StringComparer.Ordinal)))
            throw new InvalidDataException("考试词书包含未知考试分类。");
        if (Books.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != Books.Count)
            throw new InvalidDataException("考试词书 Id 重复。");
        if (Books.Select(b => b.BookId).Distinct(StringComparer.Ordinal).Count() != Books.Count)
            throw new InvalidDataException("考试词书 BookId 重复。");

        foreach (var book in Books)
        {
            if (string.IsNullOrWhiteSpace(book.Title) || string.IsNullOrWhiteSpace(book.EnglishTitle)
                || string.IsNullOrWhiteSpace(book.BookId) || book.Sections.Count == 0)
                throw new InvalidDataException($"考试词书元数据不完整：{book.BookId}。");
            if (!ExpectedBookCounts.TryGetValue(book.BookId, out var expected))
                throw new InvalidDataException($"词书 {book.BookId} 不在受支持的目录清单内。");
            if (book.AllWords.Count() != expected)
                throw new InvalidDataException($"词书 {book.BookId} 条目数应为 {expected}，实际 {book.AllWords.Count()}。");

            var prefix = "exam:" + book.BookId + ":";
            foreach (var section in book.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Id) || !section.Id.StartsWith(prefix, StringComparison.Ordinal)
                    || section.Kind != "exam:" + book.TopicId
                    || string.IsNullOrWhiteSpace(section.Title) || string.IsNullOrWhiteSpace(section.EnglishTitle))
                    throw new InvalidDataException($"考试单元结构无效：{section.Id}。");
                if (section.Entries.Count == 0 || section.Entries.Count > MaxEntriesPerSection)
                    throw new InvalidDataException($"考试单元 {section.Id} 的条目数必须在 1 到 {MaxEntriesPerSection} 之间。");
            }
        }

        foreach (var (topic, expected) in ExpectedTopicBookCounts)
            if (Books.Count(b => string.Equals(b.TopicId, topic, StringComparison.Ordinal)) != expected)
                throw new InvalidDataException($"考试分类 {topic} 的词书数量应为 {expected}。");

        var words = Books.SelectMany(b => b.AllWords).ToList();
        if (words.Count != ExpectedWordCount)
            throw new InvalidDataException($"考试词条总数应为 {ExpectedWordCount}，实际 {words.Count}。");
        if (words.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() != words.Count)
            throw new InvalidDataException("考试词条 Id 跨书或书内重复。");
        foreach (var book in Books)
        {
            foreach (var word in book.AllWords)
            {
                if (!TryParseExamWordId(word.Id, out var bookId, out _) ||
                    !string.Equals(bookId, book.BookId, StringComparison.Ordinal) ||
                    word.Words.Count != 1 || string.IsNullOrWhiteSpace(word.Word) ||
                    string.IsNullOrWhiteSpace(word.Meaning))
                    throw new InvalidDataException($"考试词条结构或书级身份无效：{word.Id}。");
            }
        }
    }

    private void BuildIndexes()
    {
        _bookIndex = Books.GroupBy(b => b.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _bookIdIndex = Books.GroupBy(b => b.BookId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _sectionIndex = Books.SelectMany(b => b.Sections.Select(s => (s.Id, Book: b)))
            .ToDictionary(pair => pair.Id, pair => pair.Book, StringComparer.Ordinal);
        _wordIndex = Books.SelectMany(b => b.AllWords)
            .ToDictionary(w => w.Id, w => w, StringComparer.Ordinal);
    }

    public LearningWord? FindWord(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (_wordIndex == null) BuildIndexes();
        return _wordIndex!.GetValueOrDefault(id.Trim());
    }

    public ExamBook? FindBook(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (_bookIndex == null) BuildIndexes();
        return _bookIndex!.GetValueOrDefault(id.Trim());
    }

    public ExamBook? FindBookBySectionId(string? sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId)) return null;
        if (_sectionIndex == null) BuildIndexes();
        return _sectionIndex!.GetValueOrDefault(sectionId.Trim());
    }

    /// <summary>按严格词条身份定位所属书，用于计划显示与跨书隔离判定。</summary>
    public ExamBook? FindBookByWordId(string? wordId)
    {
        if (!TryParseExamWordId(wordId, out var bookId, out _)) return null;
        if (_bookIdIndex == null) BuildIndexes();
        return _bookIdIndex!.GetValueOrDefault(bookId);
    }

    public List<ExamBook> BooksForTopic(string topicId) =>
        Books.Where(b => string.Equals(b.TopicId, topicId, StringComparison.Ordinal)).ToList();

    /// <summary>只接受 exam:&lt;BookId&gt;:&lt;positive ordinal&gt;，单元与计划标识不能冒充词条。</summary>
    public static bool TryParseExamWordId(string? id, out string bookId, out int ordinal)
    {
        bookId = "";
        ordinal = 0;
        if (string.IsNullOrWhiteSpace(id)) return false;
        var parts = id.Split(':');
        if (parts.Length != 3 || parts[0] != "exam" || string.IsNullOrWhiteSpace(parts[1]) ||
            parts[2].Length == 0 || parts[2][0] == '0' ||
            !parts[2].All(c => c is >= '0' and <= '9') ||
            !int.TryParse(parts[2], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out ordinal) || ordinal <= 0)
            return false;
        bookId = parts[1];
        return true;
    }

    public static bool IsExamWordId(string? id) =>
        TryParseExamWordId(id, out _, out _);
}

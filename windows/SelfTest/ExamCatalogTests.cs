using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lexi;

/// <summary>
/// 3.3.1 考试词书目录自检：数量契约、单元上限、跨书身份隔离与缺失资源的 fail-soft 行为。
/// </summary>
public static class ExamCatalogTests
{
    public static void Run()
    {
        Console.WriteLine("[exam] 考试词书目录契约与跨书身份隔离验证...");

        var missing = Path.Combine(Path.GetTempPath(), "Lexi_ExamMissing_" + Guid.NewGuid().ToString("N"));
        string? reported = null;
        var absent = ExamCatalog.TryLoad(missing, message => reported = message);
        Require(absent == null, "缺失 catalog.json 时必须失败关闭并返回 null。");
        Require(!string.IsNullOrWhiteSpace(reported), "缺失 catalog.json 时必须回报具体错误。");

        var catalog = ExamCatalog.Load();
        Require(catalog.Books.Count == ExamCatalog.ExpectedBookCount,
            $"考试词书应为 {ExamCatalog.ExpectedBookCount} 本，实际 {catalog.Books.Count}。");

        var words = catalog.Books.SelectMany(b => b.AllWords).ToList();
        Require(words.Count == ExamCatalog.ExpectedWordCount,
            $"考试词条应为 {ExamCatalog.ExpectedWordCount} 条，实际 {words.Count}。");
        Require(words.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() == words.Count,
            "考试词条身份必须全局唯一。");

        foreach (var book in catalog.Books)
        {
            foreach (var section in book.Sections)
            {
                Require(section.Entries.Count >= 1 && section.Entries.Count <= ExamCatalog.MaxEntriesPerSection,
                    $"单元 {section.Id} 的条目数超出 1..{ExamCatalog.MaxEntriesPerSection}。");
                Require(section.Entries.All(e => e.Id.StartsWith("exam:" + book.BookId + ":", StringComparison.Ordinal)),
                    $"单元 {section.Id} 的词条身份前缀与所属词书不一致。");
            }
        }

        foreach (var topic in new[] { "cet4", "cet6", "kaoyan", "gmat", "toefl", "gre" })
        {
            var books = catalog.BooksForTopic(topic);
            Require(books.Count > 0, $"考试分类 {topic} 没有可用词书。");
            Require(books.All(b => b.TopicId == topic), $"考试分类 {topic} 返回了其他分类的词书。");
        }

        var sample = catalog.Books[0].Sections[0].Entries[0];
        Require(ReferenceEquals(catalog.FindWord(sample.Id), sample), "FindWord 未按身份返回原始词条。");
        Require(ReferenceEquals(catalog.FindBookByWordId(sample.Id), catalog.Books[0]),
            "FindBookByWordId 未能定位词条所属词书。");
        Require(ReferenceEquals(catalog.FindBookBySectionId(catalog.Books[0].Sections[0].Id), catalog.Books[0]),
            "FindBookBySectionId 未能定位单元所属词书。");
        Require(catalog.FindBook(sample.Id) == null, "词条身份不应被当作词书 Id 命中。");

        Require(ExamCatalog.IsExamWordId(sample.Id), "考试词条身份必须被识别为考试词条。");
        Require(!ExamCatalog.IsExamWordId("ielts:vocabulary:1"), "IELTS 词条身份不得被当作考试词条。");
        Require(!ExamCatalog.IsExamWordId(null) && !ExamCatalog.IsExamWordId(""),
            "空身份不得被当作考试词条。");

        // 同一拼写出现在不同词书时必须是不同身份，避免计划、复习与统计串档。
        var repeated = words
            .GroupBy(w => w.Word.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Select(w => catalog.FindBookByWordId(w.Id)?.BookId)
                .Distinct(StringComparer.Ordinal).Count() > 1);
        Require(repeated != null, "未找到跨书重复拼写样本，无法验证跨书隔离。");
        var repeatedGroup = repeated!;
        Require(repeatedGroup.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() == repeatedGroup.Count(),
            "跨书重复拼写必须保持各自独立的词条身份。");

        RunIeltsChecks();

        Console.WriteLine($"      词书 {catalog.Books.Count} 本 / 词条 {words.Count} 条 / 跨书重复样本 {repeatedGroup.Count()} 条，身份隔离正常。");
    }

    /// <summary>
    /// IELTS 资料核验：章节/条目计数、写作题、书级身份隔离与音频可解析。
    /// catalog.json 缺失时（未注入受限教材的开发/构建环境）跳过并给出提示；
    /// 完整的发布包由 packaging 脚本强制要求 IELTS 资源。
    /// </summary>
    private static void RunIeltsChecks()
    {
        var catalogPath = Path.Combine(IeltsCatalog.AssetDirectory, "catalog.json");
        if (!File.Exists(catalogPath))
        {
            Console.WriteLine("[exam] 提示：Assets/IELTS/catalog.json 未注入，跳过 IELTS 内容核验。");
            return;
        }

        var ielts = IeltsCatalog.Load();
        var vocabulary = ielts.Sections.Where(s => s.Kind == "vocabulary").ToList();
        var listening = ielts.Sections.Where(s => s.Kind == "listening").ToList();
        var reading = ielts.Sections.Where(s => s.Kind == "reading").ToList();
        var spelling = ielts.Sections.Where(s => s.Kind == "spelling").ToList();

        Require(vocabulary.Count == 22, $"IELTS 词汇应为 22 个主题章节，实际 {vocabulary.Count}。");
        Require(vocabulary.Sum(s => s.Entries.Count) == 3674,
            $"IELTS 词汇应为 3674 条，实际 {vocabulary.Sum(s => s.Entries.Count)}。");
        Require(listening.Count == 1 && listening.Sum(s => s.Entries.Count) == 179,
            $"IELTS 听力考点词应为 179 条，实际 {listening.Sum(s => s.Entries.Count)}。");
        Require(reading.Count == 3 && reading.Sum(s => s.Entries.Count) == 376,
            $"IELTS 阅读同义替换应为 3 个单元 376 条，实际 {reading.Count} 个单元 {reading.Sum(s => s.Entries.Count)} 条。");
        Require(spelling.Count == 1 && spelling.Sum(s => s.Entries.Count) == 72,
            $"IELTS 英美拼写规范应为 72 条，实际 {spelling.Sum(s => s.Entries.Count)}。");
        Require(ielts.Sentences.Count == 100, $"IELTS 写作句子应为 100 题，实际 {ielts.Sentences.Count}。");
        Require(ielts.Sentences.Select(s => s.Number).Distinct().Count() == ielts.Sentences.Count,
            "IELTS 写作句子题号必须唯一。");
        Require(ielts.Sentences.All(s => !string.IsNullOrWhiteSpace(s.Chinese) && !string.IsNullOrWhiteSpace(s.BookAnswer)),
            "IELTS 写作句子必须保留题干与参考表达。");

        var allWords = ielts.AllWords.ToList();
        Require(allWords.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() == allWords.Count,
            "IELTS 词条身份必须全局唯一。");
        Require(ielts.Sections.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == ielts.Sections.Count,
            "IELTS 章节身份必须唯一。");
        Require(allWords.All(w => IsIeltsNativeWordId(w.Id)),
            "IELTS 词条必须保持原生身份，不得与 exam: 或 archive: 身份混合。");

        var orphanAudio = allWords
            .Where(w => !string.IsNullOrWhiteSpace(w.AudioPath) && IeltsCatalog.ResolveAsset(w.AudioPath) == null)
            .Take(3).ToList();
        Require(orphanAudio.Count == 0,
            $"IELTS 音频路径不可解析，例如 {string.Join(", ", orphanAudio.Select(w => w.Id))}。");
        Require(listening.Sum(s => s.Entries.Count(w => !string.IsNullOrWhiteSpace(w.AudioPath))) == 179,
            "IELTS 听力考点词必须逐条带可解析音频。");

        var manifestPath = Path.Combine(IeltsCatalog.AssetDirectory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Require(ReadManifestCount(manifest.RootElement, "chapters") == vocabulary.Count,
                "manifest.json 的词汇章节数与 catalog.json 不一致。");
            Require(ReadManifestCount(manifest.RootElement, "vocabularyEntries") == vocabulary.Sum(s => s.Entries.Count),
                "manifest.json 的词汇条目数与 catalog.json 不一致。");
            Require(ReadManifestCount(manifest.RootElement, "listeningEntries") == listening.Sum(s => s.Entries.Count),
                "manifest.json 的听力条目数与 catalog.json 不一致。");
            Require(ReadManifestCount(manifest.RootElement, "readingEntries") == reading.Sum(s => s.Entries.Count),
                "manifest.json 的阅读条目数与 catalog.json 不一致。");
            Require(ReadManifestCount(manifest.RootElement, "spellingEntries") == spelling.Sum(s => s.Entries.Count),
                "manifest.json 的拼写条目数与 catalog.json 不一致。");
            Require(ReadManifestCount(manifest.RootElement, "writingSentences") == ielts.Sentences.Count,
                "manifest.json 的写作题数与 catalog.json 不一致。");
        }

        Console.WriteLine(
            $"      IELTS 22 章 {vocabulary.Sum(s => s.Entries.Count)} 词 / 听力 {listening.Sum(s => s.Entries.Count)} / " +
            $"阅读 {reading.Sum(s => s.Entries.Count)} / 拼写 {spelling.Sum(s => s.Entries.Count)} / 写作 {ielts.Sentences.Count}，身份隔离正常。");
    }

    private static bool IsIeltsNativeWordId(string id) =>
        id.StartsWith("vocab:", StringComparison.Ordinal) ||
        id.StartsWith("listening:", StringComparison.Ordinal) ||
        id.StartsWith("reading:", StringComparison.Ordinal) ||
        id.StartsWith("spelling:", StringComparison.Ordinal);

    private static int ReadManifestCount(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || !value.TryGetInt32(out var count))
            throw new InvalidOperationException($"manifest.json 缺少整数字段 {property}。");
        return count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using Microsoft.Data.Sqlite;

namespace Lexi.Tests;

/// <summary>
/// 3.3.1 冷启动页面恢复的存储层回归：LastPage 白名单往返、窄写入保留未知字段、
/// 不触碰已保存凭据、损坏 JSON 不被覆盖。
/// </summary>
internal static class NavigationTests
{
    private const string CredentialFixture = "navigation-credential-fixture";

    internal static void Run()
    {
        WhitelistRules();
        StorageRoundTrip();
    }

    private static void WhitelistRules()
    {
        foreach (var page in new[] { "lookup", "vocab", "learning", "ielts", "exams", "statistics" })
        {
            Program.Check(NavigationState.IsRestorable(page), $"白名单页面可恢复：{page}");
            Program.Check(NavigationState.Normalize(page) == page, $"白名单页面原样保留：{page}");
        }
        foreach (var page in new[] { "review", "focus", "social", "settings", "quotes", "LOOKUP", " lookup", "unknown" })
        {
            Program.Check(!NavigationState.IsRestorable(page), $"非冷启动页面不恢复：{page}");
            Program.Check(NavigationState.Normalize(page) == "lookup", $"非法页面回落查词页：{page}");
        }
        Program.Check(NavigationState.Normalize(null) == "lookup", "空值回落查词页");
        Program.Check(NavigationState.Normalize("") == "lookup", "空字符串回落查词页");
    }

    private static void StorageRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lexi-navigation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "fixture.sqlite3");
        try
        {
            using (var store = new VocabularyService(db))
            {
                store.SaveSettings(new AppSettings { LastPage = "exams", Theme = "Dark" });
                Program.Check(store.LoadSettings().LastPage == "exams", "SaveSettings 持久化合法 LastPage");
                store.SaveSettings(new AppSettings { LastPage = "review" });
                Program.Check(store.LoadSettings().LastPage == "lookup", "SaveSettings 将白名单外 LastPage 归一回查词页");
                store.SaveSettings(new AppSettings { ApiKey = CredentialFixture, RememberKey = true, LastPage = "ielts" });
                store.SaveNavigationState("statistics");
                Program.Check(store.LoadSettings().LastPage == "statistics", "SaveNavigationState 更新 LastPage");
                Program.Check(store.LoadSettings().ApiKey == CredentialFixture, "窄写入不得改动已保存凭据");
                var settledJson = ReadSettingsJson(db);
                store.SaveNavigationState("statistics");
                Program.Check(ReadSettingsJson(db) == settledJson, "重复窄写入不产生内容变化");
            }

            using (var store = new VocabularyService(db))
                Program.Check(store.LoadSettings().LastPage == "statistics", "重启后 LastPage 保留");

            WriteSettingsJson(db, """{"Theme":"Dark","FutureField":"keep"}""");
            using (var store = new VocabularyService(db))
                store.SaveNavigationState("vocab");
            Program.Check(ReadSettingsJson(db).Contains("\"FutureField\":\"keep\"", StringComparison.Ordinal),
                "窄写入保留未来版本未知字段");
            using (var store = new VocabularyService(db))
                Program.Check(store.LoadSettings().LastPage == "vocab", "窄写入后的 LastPage 可加载");

            WriteSettingsJson(db, "{");
            using (var store = new VocabularyService(db))
                store.SaveNavigationState("exams");
            Program.Check(ReadSettingsJson(db) == "{", "损坏 JSON 不被导航写入覆盖");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string ReadSettingsJson(string db)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT settings_json FROM app_settings WHERE id = 1";
        return cmd.ExecuteScalar() as string ?? "";
    }

    private static void WriteSettingsJson(string db, string json)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = db, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE app_settings SET settings_json = $json WHERE id = 1";
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }
}

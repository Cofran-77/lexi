using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace Lexi;

public static class DatabaseSafetyTests
{
    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "LexiSafety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "vocab.sqlite3");
            var orphan = Path.Combine(folder, "orphan.sqlite3");
            File.WriteAllText(orphan + "-wal", "preserve unmatched old WAL");
            var orphanRejected = false;
            try { using var bad = new VocabularyService(orphan); } catch (InvalidDataException) { orphanRejected = true; }
            Check(orphanRejected && !File.Exists(orphan) && File.ReadAllText(orphan + "-wal") == "preserve unmatched old WAL", "orphaned WAL blocks new database creation without deletion");
            using (var service = new VocabularyService(path))
            {
                service.AddWord("durable", "/ˈdjʊərəbl/", "耐久的", "lasting");
                Check(Directory.Exists(Path.Combine(folder, "backups")) && Directory.GetFiles(Path.Combine(folder, "backups"), "*.sqlite3").Length > 0, "committed word has a standalone automatic backup");
                var newest = Directory.GetFiles(Path.Combine(folder, "backups"), "*.sqlite3").OrderDescending().First();
                using var backup = Open(newest);
                DatabaseSafety.Validate(backup);
                Check(Scalar(backup, "SELECT count(*) FROM words WHERE word='durable'") == "1", "backup contains committed word");
                Check(Scalar(backup, "PRAGMA journal_mode") == "delete", "backup does not depend on WAL sidecars");
            }
            // Exercise repeated upgrade-like close/reopen cycles with settings + edits.
            for (var i = 0; i < 24; i++)
            {
                using var service = new VocabularyService(path);
                Check(service.GetAllWords().Count == 1, "reopen preserves words");
                service.EditWord(service.GetAllWords()[0].Id, "durable-" + i);
                service.SaveSettings(new AppSettings { Theme = i % 2 == 0 ? "Light" : "Dark", ApiKey = "not-persisted", RememberKey = false });
            }
            using (var check = Open(path))
            {
                DatabaseSafety.Validate(check);
                Check(Scalar(check, "SELECT translation FROM words") == "durable-23", "repeated reopen preserves latest edits");
                Check(Scalar(check, "SELECT settings_json FROM app_settings").Contains("not-persisted") == false, "backups do not enable key persistence");
            }
            // Explicitly capture the latest edit; routine backups now use a time interval.
            using (var current = new VocabularyService(path)) current.CreateManualBackup();
            Check(Directory.GetFiles(Path.Combine(folder, "backups"), "*.sqlite3").Length <= 20, "snapshot retention bounded");
            // A corrupt preexisting file must fail before schema/init or backup rotation.
            var corrupt = Path.Combine(folder, "corrupt.sqlite3");
            File.WriteAllText(corrupt, "not a database, preserve this evidence");
            var before = SHA256.HashData(File.ReadAllBytes(corrupt));
            var rejected = false;
            try { using var bad = new VocabularyService(corrupt); } catch { rejected = true; }
            Check(rejected && before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(corrupt))), "corrupt startup fails without overwriting original");
            var restoreFrom = Directory.GetFiles(Path.Combine(folder, "backups"), "*.sqlite3").OrderDescending().First();
            var archive = DatabaseSafety.RestoreBackup(restoreFrom, corrupt);
            Check(File.ReadAllText(Path.Combine(archive, "corrupt.sqlite3")) == "not a database, preserve this evidence", "restore archives corrupt evidence");
            using (var restored = Open(corrupt))
            {
                DatabaseSafety.Validate(restored);
                Check(Scalar(restored, "SELECT translation FROM words") == "durable-23", "explicit restore preserves snapshot contents");
            }
            var badBackup = Path.Combine(folder, "bad-backup.sqlite3");
            File.WriteAllText(badBackup, "invalid backup");
            var oldHash = SHA256.HashData(File.ReadAllBytes(corrupt));
            rejected = false;
            try { DatabaseSafety.RestoreBackup(badBackup, corrupt); } catch { rejected = true; }
            Check(rejected && oldHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(corrupt))), "invalid recovery candidate cannot replace data");
            var incomplete = Path.Combine(folder, "incomplete.sqlite3");
            using (var missing = Open(incomplete))
            {
                using var cmd = missing.CreateCommand();
                cmd.CommandText = "CREATE TABLE words(id,word,translation,stage,status,next_review_date,review_count);CREATE TABLE app_settings(settings_json);CREATE TABLE review_logs(id,word_id);";
                cmd.ExecuteNonQuery();
            }
            rejected = false;
            try { DatabaseSafety.RestoreBackup(incomplete, corrupt); } catch { rejected = true; }
            Check(rejected && oldHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(corrupt))), "physically valid backup missing app columns is rejected");
            var interrupted = Path.Combine(folder, "interrupted.sqlite3");
            File.WriteAllText(interrupted + ".restore-pending.json", "{\"interrupted\":true}");
            rejected = false;
            try { using var bad = new VocabularyService(interrupted); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && !File.Exists(interrupted), "interrupted restore never silently creates empty vocabulary");
            // 阶段化恢复现场：只有明确阶段才自动收尾；未知记录仍然 fail-closed（上面已覆盖）。
            var staged = Path.Combine(folder, "staged.sqlite3");
            using (var service = new VocabularyService(staged)) service.AddWord("staged-word", "/st/", "候选前中断", "before candidate");
            var stagedCandidate = Path.Combine(folder, ".restore-staged-candidate.sqlite3");
            File.Copy(staged, stagedCandidate);
            WriteMarker(staged, "staged", stagedCandidate, Path.Combine(folder, "staged-archive"));
            using (var service = new VocabularyService(staged)) Check(service.GetAllWords().Count == 1, "staged 中断保留原词库");
            Check(!File.Exists(stagedCandidate) && !File.Exists(staged + ".restore-pending.json"), "staged 中断丢弃未生效候选");

            var moving = Path.Combine(folder, "moving.sqlite3");
            using (var service = new VocabularyService(moving)) service.AddWord("moving-word", "/mv/", "回滚", "rollback");
            var movingArchive = Path.Combine(folder, "moving-archive");
            Directory.CreateDirectory(movingArchive);
            var movingCandidate = Path.Combine(folder, ".restore-moving-candidate.sqlite3");
            File.Copy(moving, movingCandidate);
            File.Move(moving, Path.Combine(movingArchive, "moving.sqlite3"));
            WriteMarker(moving, "moving", movingCandidate, movingArchive);
            using (var service = new VocabularyService(moving))
                Check(service.GetAllWords().Single().Word == "moving-word", "moving 中断回滚到恢复前词库");
            Check(!File.Exists(moving + ".restore-pending.json") && !File.Exists(movingCandidate), "moving 中断清理候选与现场记录");

            var installing = Path.Combine(folder, "installing.sqlite3");
            using (var service = new VocabularyService(installing)) service.AddWord("old-word", "/old/", "旧", "old");
            var installingArchive = Path.Combine(folder, "installing-archive");
            Directory.CreateDirectory(installingArchive);
            File.Move(installing, Path.Combine(installingArchive, "installing.sqlite3"));
            var installingCandidate = Path.Combine(folder, "installing-candidate.sqlite3");
            using (var service = new VocabularyService(installingCandidate)) service.AddWord("new-word", "/new/", "新", "new");
            WriteMarker(installing, "installing", installingCandidate, installingArchive);
            using (var service = new VocabularyService(installing))
                Check(service.GetAllWords().Single().Word == "new-word", "installing 中断完成安装");
            Check(!File.Exists(installing + ".restore-pending.json") && !File.Exists(installingCandidate), "installing 中断清理现场");

            var installed = Path.Combine(folder, "installed.sqlite3");
            using (var service = new VocabularyService(installed)) service.AddWord("installed-word", "/in/", "已安装", "installed");
            WriteMarker(installed, "installed", Path.Combine(folder, ".restore-missing-candidate.sqlite3"), Path.Combine(folder, "installed-archive"));
            using (var service = new VocabularyService(installed))
                Check(service.GetAllWords().Single().Word == "installed-word", "installed 中断保留已安装词库");
            Check(!File.Exists(installed + ".restore-pending.json"), "installed 中断清理现场记录");

            // 恢复旧快照时，未确认的 JSON 重放必须失效，否则过期计划/进度会覆盖较新的 JSON 文件。
            var outboxSource = Path.Combine(folder, "outbox-source.sqlite3");
            string outboxBackup;
            using (var service = new VocabularyService(outboxSource))
            {
                service.AddWord("outbox-word", "/ob/", "重放", "replay");
                using (var raw = Open(outboxSource))
                using (var insert = raw.CreateCommand())
                {
                    insert.CommandText = "INSERT INTO mutation_outbox (kind,payload_json,created_at_utc,applied_at_utc,attempts) VALUES ('json.snapshot.v1','{}',$at,NULL,0)";
                    insert.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
                    insert.ExecuteNonQuery();
                }
                outboxBackup = service.CreateManualBackup();
            }
            var outboxTarget = Path.Combine(folder, "outbox-target.sqlite3");
            using (var service = new VocabularyService(outboxTarget)) { }
            DatabaseSafety.RestoreBackup(outboxBackup, outboxTarget);
            using (var restored = Open(outboxTarget))
                Check(Scalar(restored, "SELECT count(*) FROM mutation_outbox WHERE applied_at_utc IS NULL") == "0",
                    "恢复旧快照时未确认 JSON 重放被失效");
            using (var guard = new InstallationGuard(folder))
            {
                rejected = false;
                try { using var second = new InstallationGuard(folder); } catch (IOException) { rejected = true; }
                Check(rejected, "running/install guard excludes another holder");
            }
            using (var guard = new InstallationGuard(folder)) { }
            Console.WriteLine("PASS: automatic standalone backups, 24 reopen/write cycles, retention, fail-closed corruption guard");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }

    private static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); c.Open(); return c;
    }
    private static void WriteMarker(string target, string stage, string candidate, string archive) =>
        DatabaseSafety.WriteRestoreMarker(target + ".restore-pending.json",
            new DatabaseSafety.RestoreMarker(DatabaseSafety.RestoreMarkerFormat, stage, target, candidate, archive, [target]),
            createNew: true);
    private static string Scalar(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar()?.ToString() ?? ""; }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("Safety regression: " + message); }
}

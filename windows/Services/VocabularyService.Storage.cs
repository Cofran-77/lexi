using Microsoft.Data.Sqlite;

namespace Lexi;

/// <summary>
/// 3.3.1 累计升级里的存储治理（对齐 macOS 3.2.2 行为，不改任何业务行）：
/// 启动时先判断是否需要“维护前备份”，再决定是否删除已应用的跨存储快照并在必要时 VACUUM。
/// 逐词学习历史、FSRS 状态、金句、计划一律不参与清理。
/// </summary>
public sealed partial class VocabularyService
{
    private bool NeedsStorageMaintenanceBackup()
    {
        using var cmd = _connection.CreateCommand();
        long Pragma(string name)
        {
            cmd.CommandText = "PRAGMA " + name;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        var free = Pragma("freelist_count");
        var pages = Pragma("page_count");
        var pageSize = Pragma("page_size");
        // A previous launch may have deleted snapshots before VACUUM ran out
        // of space. Rotate a validated backup before retrying that maintenance.
        if (free * pageSize >= 32L * 1024 * 1024 && free >= pages / 4) return true;
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='mutation_outbox'";
        if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return false;
        cmd.CommandText = "SELECT COALESCE(SUM(length(CAST(payload_json AS BLOB))),0) FROM mutation_outbox WHERE applied_at_utc IS NOT NULL AND kind=$kind";
        cmd.Parameters.AddWithValue("$kind", CrossStoreJournal.MutationKind);
        return Convert.ToInt64(cmd.ExecuteScalar()) >= 32L * 1024 * 1024;
    }

    /// <summary>3.2.2 兼容性维护；不删除业务行、不改 schema。</summary>
    private void MaintainStorage()
    {
        try
        {
            int removed;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM mutation_outbox WHERE applied_at_utc IS NOT NULL AND kind = $kind";
                cmd.Parameters.AddWithValue("$kind", CrossStoreJournal.MutationKind);
                removed = cmd.ExecuteNonQuery();
            }
            using var maintenance = _connection.CreateCommand();
            long Pragma(string name)
            {
                maintenance.CommandText = "PRAGMA " + name;
                return Convert.ToInt64(maintenance.ExecuteScalar());
            }
            var free = Pragma("freelist_count");
            var pages = Pragma("page_count");
            var pageSize = Pragma("page_size");
            var compact = free * pageSize >= 32L * 1024 * 1024 && free >= pages / 4;
            if (compact)
            {
                // SQLite owns rollback and file replacement. Never delete a live
                // database/sidecar or rewrite memory state to reclaim space.
                maintenance.CommandText = "VACUUM";
                maintenance.ExecuteNonQuery();
                DatabaseSafety.Validate(_connection);
            }
            if (removed > 0 || compact) BackupCommittedState(force: true, keepOnlyLatest: true);
        }
        catch (Exception ex) when (ex is IOException or SqliteException or UnauthorizedAccessException)
        {
            // Freed pages remain reusable even if disk space blocks VACUUM.
            // Retry next launch; normal committed learning data is still usable.
            BackupWarning = "数据已保存，但存储整理未完成；下次启动将重试。请检查磁盘空间与权限。";
        }
    }
}

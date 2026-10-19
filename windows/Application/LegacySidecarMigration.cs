namespace Lexi;

/// <summary>
/// Additive migration for 3.1.3 JSON sidecars. Existing destination files are never
/// replaced, and source files are never moved or modified.
/// </summary>
public static class LegacySidecarMigration
{
    public static readonly IReadOnlyList<string> FileNames =
    [
        "daily-plans.json",
        "daily-study-plans.json",
        "ielts-learning.json",
        "learning-progress.json",
    ];

    public static bool TryMigrate(string sourceDirectory, string targetDirectory, out string message)
    {
        message = "";
        string? temporary = null;
        try
        {
            var sourceRoot = Path.GetFullPath(sourceDirectory);
            var targetRoot = Path.GetFullPath(targetDirectory);
            if (string.Equals(sourceRoot.TrimEnd(Path.DirectorySeparatorChar),
                    targetRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                message = "旧版与新版使用同一数据目录，无需复制计划侧写。";
                return false;
            }

            var copied = 0;
            var skipped = 0;
            foreach (var fileName in FileNames)
            {
                var source = Path.Combine(sourceRoot, fileName);
                var target = Path.Combine(targetRoot, fileName);
                if (!File.Exists(source)) continue;
                if (File.Exists(target))
                {
                    skipped++;
                    continue;
                }

                Directory.CreateDirectory(targetRoot);
                temporary = Path.Combine(targetRoot, $".lexi-sidecar-{Guid.NewGuid():N}.tmp");
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                           FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                File.Move(temporary, target, overwrite: false);
                temporary = null;
                copied++;
            }

            message = copied > 0
                ? $"已安全复制 {copied} 个旧版计划侧写文件；原文件保持不变。"
                : skipped > 0
                    ? "新版计划侧写已存在，未覆盖任何文件。"
                    : "未检测到需要迁移的旧版计划侧写。";
            return copied > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            message = "旧版计划侧写复制失败，现有文件均保留：" + ex.Message;
            return false;
        }
        finally
        {
            if (temporary is not null)
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}

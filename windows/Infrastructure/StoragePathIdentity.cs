namespace Lexi;

/// <summary>Resolves a path to its physical identity so that linked or
/// differently-cased directories cannot be used to bypass containment checks.</summary>
public static class StoragePathIdentity
{
    public static string Resolve(string path) => Resolve(path, 0);

    // Resolve existing ancestors before creating anything, including links whose final child does not yet exist.
    private static string Resolve(string path, int depth = 0)
    {
        if (depth > 40) throw new IOException("档案路径链接过多；原数据已保留。");
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var resolved = root;
        var remainder = full[root.Length..]
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in remainder)
        {
            var candidate = Path.Combine(resolved, part);
            // Windows volumes are case-insensitive by default; prefer the volume's actual casing,
            // but keep an exact-case match first so case-sensitive volumes stay distinguishable.
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                var names = Directory.EnumerateFileSystemEntries(resolved).Select(Path.GetFileName).ToArray();
                var actual = names.FirstOrDefault(name => string.Equals(name, part, StringComparison.Ordinal))
                    ?? names.FirstOrDefault(name => string.Equals(name, part, StringComparison.OrdinalIgnoreCase));
                if (actual is null) throw new IOException("档案路径实际名称无法确认；原数据已保留。");
                candidate = Path.Combine(resolved, actual);
            }
            resolved = candidate;
            FileSystemInfo? existing = Directory.Exists(resolved) ? new DirectoryInfo(resolved)
                : File.Exists(resolved) ? new FileInfo(resolved)
                : null;
            if (existing?.LinkTarget is not null)
                resolved = Resolve(existing.ResolveLinkTarget(true)?.FullName ?? throw new IOException("档案路径无法确认；原数据已保留。"), depth + 1);
        }
        var trimmed = Path.GetFullPath(resolved).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Never degrade a volume root such as "C:\" into a drive-relative "C:".
        if (trimmed.Length < resolved.Length) trimmed += Path.DirectorySeparatorChar;
        return trimmed.Length == 0 ? resolved : trimmed;
    }
}

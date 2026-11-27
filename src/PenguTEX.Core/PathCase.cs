using System.Collections.Concurrent;

namespace PenguTEX.Core;

/// <summary>
/// Whether a host path lives on a volume that ignores filename case. Windows always does; on macOS the default APFS
/// volume does too, so "/work/Result" and "/work/result" name one directory. Linux usually does not, but a casefold or
/// FAT/NTFS mount can, so non-Windows hosts probe the filesystem instead of assuming.
/// </summary>
public static class PathCase
{
    private static readonly ConcurrentDictionary<string, bool> Probed = new(StringComparer.Ordinal);

    public static bool IgnoresCase(string path)
        => OperatingSystem.IsWindows() || Probed.GetOrAdd(Path.GetFullPath(path), Probe);

    public static StringComparison Comparison(string path)
        => IgnoresCase(path) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Stable key for hashing a canonical path: case-folded where the volume ignores case.</summary>
    public static string Key(string canonical) => IgnoresCase(canonical) ? canonical.ToUpperInvariant() : canonical;

    private static bool Probe(string full)
    {
        // Nearest existing ancestor: the path itself may be an output folder that does not exist yet.
        string? current = full;
        while (current is not null && !Path.Exists(current)) current = Path.GetDirectoryName(current);
        for (; current is not null; current = Path.GetDirectoryName(current))
        {
            string name = Path.GetFileName(current);
            if (Swap(name) is { } swapped)
                return Path.Exists(Path.Combine(Path.GetDirectoryName(current)!, swapped));
            // No cased letter in this name (for example "/" or "2026"): probe an entry inside it instead.
            if (Directory.Exists(current))
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                        if (Swap(Path.GetFileName(entry)) is { } child) return Path.Exists(Path.Combine(current, child));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return false;
    }

    private static string? Swap(string name)
    {
        var chars = name.ToCharArray(); bool changed = false;
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i], s = char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
            if (s != c) { chars[i] = s; changed = true; }
        }
        return changed ? new string(chars) : null;
    }
}

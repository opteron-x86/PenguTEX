using System.Text.RegularExpressions;

namespace PenguTEX.Core;

public sealed record PruneResult(int Files, long Bytes, int KeptFiles, long KeptBytes, int OtherFiles, bool DryRun);

/// <summary>The work cache holds CPU-generated textures only, under a versioned subfolder.</summary>
public static partial class CacheStore
{
    public const string Layout = "v2";
    public static string Folder(string cache) => Path.Combine(cache, Layout);

    [GeneratedRegex(@"^[0-9A-F]{64}\.dds(\.json)?$")] private static partial Regex Entry();
    [GeneratedRegex(@"^[0-9A-F]{64}\.dds(\.json)?\.[0-9a-f]{32}\.tmp$")] private static partial Regex Temporary();
    [GeneratedRegex(@"^job-[0-9A-F]{20}\.json$")] private static partial Regex Journal();

    /// <summary>
    /// Removes files PenguTEX itself wrote and no longer needs: everything from the older flat layout,
    /// current entries no build has used for <paramref name="days"/> days, and abandoned temporaries.
    /// Files with any other name are never touched.
    /// </summary>
    public static PruneResult Prune(string cache, int days = 30, bool dryRun = false, DateTime? now = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cache)) throw new ArgumentException("Choose a cache folder.");
        if (days < 0) throw new ArgumentException("Days must be zero or more.");
        var root = new DirectoryInfo(Path.GetFullPath(cache));
        if (!root.Exists) return new(0, 0, 0, 0, 0, dryRun);
        if (root.LinkTarget is not null) throw new IOException("The cache folder cannot be a symbolic link.");
        if (File.Exists(Path.Combine(root.FullName, JobRunner.ManifestName))) throw new IOException("This folder is a PenguTEX output, not a work cache.");
        DateTime time = now ?? DateTime.UtcNow, cutoff = time.AddDays(-days), stale = time.AddDays(-1);
        int files = 0, kept = 0, other = 0; long bytes = 0, keptBytes = 0;
        void Remove(FileInfo file)
        {
            files++; bytes += file.Length;
            if (!dryRun) file.Delete();
        }
        foreach (var file in root.EnumerateFiles())
        {
            ct.ThrowIfCancellationRequested();
            if (file.LinkTarget is null && (Entry().IsMatch(file.Name) || Temporary().IsMatch(file.Name) || Journal().IsMatch(file.Name))) Remove(file);
            else other++;
        }
        var store = new DirectoryInfo(Folder(root.FullName));
        if (store.Exists && store.LinkTarget is null)
            foreach (var file in store.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                if (file.LinkTarget is not null) { other++; continue; }
                if (Temporary().IsMatch(file.Name))
                {
                    // A running build may still own a recent temporary.
                    if (days == 0 || file.LastWriteTimeUtc < stale) Remove(file); else { kept++; keptBytes += file.Length; }
                }
                else if (Entry().IsMatch(file.Name))
                {
                    // A build refreshes the metadata time of every entry it reuses; the texture follows its metadata.
                    string texture = file.Name.EndsWith(".json", StringComparison.Ordinal) ? file.Name[..^5] : file.Name;
                    var metadata = new FileInfo(Path.Combine(store.FullName, texture + ".json"));
                    DateTime used = metadata.Exists ? metadata.LastWriteTimeUtc : file.LastWriteTimeUtc;
                    if (days == 0 || used < cutoff) Remove(file); else { kept++; keptBytes += file.Length; }
                }
                else other++;
            }
        return new(files, bytes, kept, keptBytes, other, dryRun);
    }
}

using System.Diagnostics;
using System.Security.Cryptography;

namespace PenguTEX.Core;

public sealed record OutputEntry(string Path, string Source, string SourceHash, string OutputHash,
    int Width, int Height, int DropMips, long Bytes, string Recipe);
public sealed record OutputManifest(string Product, int Schema, string Version, DateTimeOffset Created,
    string PolicyHash, IReadOnlyList<OutputEntry> Files, int Unchanged, int Protected, int Unsupported, int Invalid);
public sealed record BuildResult(string Output, int Files, int Reused, long Bytes, double Seconds, OutputManifest Manifest);

public static class JobRunner
{
    public const string ManifestName = "pengutex-manifest.json";
    public const string Product = "PenguTEX";
    public const string RecipeVersion = "mip-copy-v1";

    private static string Canonical(string path)
    {
        string full = Path.GetFullPath(path), current = Path.GetPathRoot(full)!;
        foreach (var part in Path.GetRelativePath(current, full).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Broken link: " + current);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }
    private static bool Within(string child, string parent)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        child = Canonical(child); parent = Canonical(parent);
        return child.Equals(parent, comparison) || child.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
    }
    public static string Resolve(string root, string path)
    {
        string p = path.Replace('\\', '/');
        if (!p.StartsWith("textures/", StringComparison.Ordinal) || p.Split('/').Any(x => x.Length == 0 || x is "." or ".." || x.Any(c => c < 32 || "<>:\"|?*".Contains(c))))
            throw new InvalidDataException("Unsafe output path: " + path);
        string full = Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar));
        if (!Within(full, root)) throw new IOException("Output path escapes its root: " + path);
        return full;
    }
    public static string Hash(Stream stream, CancellationToken ct = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[128 * 1024]; int count;
        while ((count = stream.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer.AsSpan(0, count)); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static string HashFile(string file, CancellationToken ct) { using var f = File.OpenRead(file); return Hash(f, ct); }

    public static BuildResult Build(Catalog catalog, OptimizationPlan plan, string output, string cache,
        int workers = 2, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        if (plan.Schema != 1 || !plan.CompleteSource || catalog.Diagnostics.Any(d => d.Severity == "Error"))
            throw new InvalidDataException("Resolve source errors before building an output mod.");
        if (workers is < 1 or > 32) throw new ArgumentException("Workers must be between 1 and 32.");
        if (new DirectoryInfo(Path.GetFullPath(output)).LinkTarget is not null || new DirectoryInfo(Path.GetFullPath(cache)).LinkTarget is not null)
            throw new IOException("Output/cache roots cannot be symbolic links.");
        output = Canonical(output); cache = Canonical(cache);
        if (Within(output, cache) || Within(cache, output)) throw new IOException("Output and cache must be separate directories.");
        foreach (var root in catalog.Roots)
            if (Within(output, root) || Within(root, output) || Within(cache, root) || Within(root, cache))
                throw new IOException("Output/cache must not overlap source roots.");
        foreach (var asset in catalog.Assets)
            if (Within(asset.Container, output) || Within(asset.Container, cache)) throw new IOException("A source resolves inside output/cache.");
        string parent = Path.GetDirectoryName(output) ?? throw new IOException("Output must have a parent directory.");
        Directory.CreateDirectory(parent); Directory.CreateDirectory(cache);
        string identity = JsonFiles.Hash(output)[..20];
        using var outputLock = new FileStream(Path.Combine(parent, ".pengutex-" + identity + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string stage = Path.Combine(parent, ".pengutex-stage-" + identity);
        string backup = Path.Combine(parent, ".pengutex-previous-" + identity);
        Recover(output, backup);
        if (Directory.Exists(output)) CheckOwnedTree(output, EnsureOwned(output));
        if (Directory.Exists(stage)) { EnsureOwnedStage(stage); Directory.Delete(stage, true); }
        Directory.CreateDirectory(stage); File.WriteAllText(Path.Combine(stage, ".pengutex-stage"), Product);
        var watch = Stopwatch.StartNew(); string policyHash = JsonFiles.Hash(System.Text.Json.JsonSerializer.Serialize(plan.Policy, JsonFiles.Options));
        var lookup = catalog.Assets.ToDictionary(a => a.Path, StringComparer.Ordinal);
        var changed = plan.Items.Where(i => i.Decision == Decision.Reduce).ToArray();
        var entries = new OutputEntry[changed.Length]; int done = 0, reused = 0;
        var stateGate = new object(); var completed = new Dictionary<string, OutputEntry>();
        string journal = Path.Combine(cache, "job-" + identity + ".json");
        Parallel.For(0, changed.Length, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct }, i =>
        {
            var item = changed[i]; ct.ThrowIfCancellationRequested();
            if (!lookup.TryGetValue(item.Path, out var asset) || asset.Container != item.Container)
                throw new IOException("Source winner changed; analyze again: " + item.Path);
            string hash;
            using (var stream = asset.Open()) hash = Hash(stream, ct);
            string recipe = JsonFiles.Hash(hash + "|" + RecipeVersion + "|" + item.DropMips + "|" + policyHash);
            string cached = Path.Combine(cache, recipe + ".dds"), metadata = cached + ".json";
            string dest = Resolve(stage, item.Path); Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            OutputEntry? entry = null;
            if (File.Exists(cached) && File.Exists(metadata))
            {
                try
                {
                    var candidate = JsonFiles.Read<OutputEntry>(metadata);
                    using var check = File.OpenRead(cached); var info = Dds.Inspect(check).Info;
                    if (candidate.Recipe == recipe && candidate.SourceHash == hash && info.Width == item.TargetWidth
                        && info.Height == item.TargetHeight && HashFile(cached, ct) == candidate.OutputHash)
                        entry = candidate with { Path = item.Path, Source = item.Origin };
                }
                catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or NotSupportedException) { }
            }
            if (entry is not null)
            {
                File.Copy(cached, dest); Interlocked.Increment(ref reused);
            }
            else
            {
                using (var source = asset.Open())
                {
                    if (source.CanSeek)
                    {
                        var current = Dds.Inspect(source, asset.Length).Info;
                        if (current != item.Input) throw new IOException("Source metadata changed; analyze again: " + item.Path);
                        source.Position = 0;
                    }
                    using var target = File.Create(dest); Dds.Reduce(source, target, item.DropMips, asset.Length, ct, item.Input);
                }
                using var verify = File.OpenRead(dest); var info = Dds.Inspect(verify).Info;
                if (info.Width != item.TargetWidth || info.Height != item.TargetHeight || info.PayloadBytes != item.OutputPayload)
                    throw new InvalidDataException("Generated texture does not match the plan: " + item.Path);
                entry = new(item.Path, item.Origin, hash, HashFile(dest, ct), info.Width, info.Height, item.DropMips, verify.Length, recipe);
                string temporary = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(dest, temporary); File.Move(temporary, cached, true); JsonFiles.Write(metadata, entry); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            using (var source = asset.Open())
                if (Hash(source, ct) != hash) throw new IOException("Source changed during processing: " + item.Path);
            entries[i] = entry;
            lock (stateGate)
            {
                completed[item.Path] = entry;
                if (completed.Count % 100 == 0 || completed.Count == changed.Length) JsonFiles.Write(journal, completed.Values.ToArray());
            }
            progress?.Report(new("Build", Interlocked.Increment(ref done), changed.Length, item.Path, entry.Recipe));
        });
        ct.ThrowIfCancellationRequested();
        var manifest = new OutputManifest(Product, 1, typeof(JobRunner).Assembly.GetName().Version?.ToString() ?? "0.1.0", DateTimeOffset.UtcNow,
            policyHash, entries, plan.Items.Count(x => x.Decision == Decision.Keep), plan.Items.Count(x => x.Decision == Decision.Protected),
            plan.Items.Count(x => x.Decision == Decision.Unsupported), plan.Items.Count(x => x.Decision == Decision.Invalid));
        JsonFiles.Write(Path.Combine(stage, ManifestName), manifest);
        File.WriteAllText(Path.Combine(stage, "meta.ini"), "[General]\ncomments=Generated by PenguTEX. See pengutex-manifest.json.\n");
        Verify(stage, ct); ct.ThrowIfCancellationRequested();
        if (Directory.Exists(output)) Directory.Move(output, backup);
        try { Directory.Move(stage, output); }
        catch { if (Directory.Exists(backup) && !Directory.Exists(output)) Directory.Move(backup, output); throw; }
        if (Directory.Exists(backup)) { EnsureOwned(backup); Directory.Delete(backup, true); }
        return new(output, entries.Length, reused, entries.Sum(e => e.Bytes), watch.Elapsed.TotalSeconds, manifest);
    }

    private static void EnsureOwnedStage(string path)
    {
        if (new DirectoryInfo(path).LinkTarget is not null || !File.Exists(Path.Combine(path, ".pengutex-stage"))
            || File.ReadAllText(Path.Combine(path, ".pengutex-stage")) != Product) throw new IOException("Unrecognized staging directory: " + path);
    }
    private static OutputManifest EnsureOwned(string path)
    {
        if (new DirectoryInfo(path).LinkTarget is not null) throw new IOException("Output cannot be a symlink.");
        var manifest = JsonFiles.Read<OutputManifest>(Path.Combine(path, ManifestName));
        if (manifest.Product != Product || manifest.Schema != 1) throw new IOException("Not a recognized PenguTEX output.");
        return manifest;
    }
    private static void Recover(string output, string backup)
    {
        if (!Directory.Exists(backup)) return;
        CheckOwnedTree(backup, EnsureOwned(backup));
        if (!Directory.Exists(output)) Directory.Move(backup, output);
        else { Verify(output); Directory.Delete(backup, true); }
    }
    public static OutputManifest Verify(string output, CancellationToken ct = default)
    {
        var manifest = EnsureOwned(output); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CheckOwnedTree(output, manifest);
        foreach (var file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested(); if (!seen.Add(file.Path)) throw new InvalidDataException("Duplicate output entry.");
            var path = Resolve(output, file.Path); using var stream = File.OpenRead(path); var info = Dds.Inspect(stream).Info;
            if (stream.Length != file.Bytes || info.Width != file.Width || info.Height != file.Height || HashFile(path, ct) != file.OutputHash)
                throw new InvalidDataException("Output verification failed: " + file.Path);
        }
        return manifest;
    }
    private static void CheckOwnedTree(string root, OutputManifest manifest)
    {
        var allowed = manifest.Files.Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
        allowed.UnionWith([ManifestName, "meta.ini", ".pengutex-stage"]);
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(stack.Pop()))
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (info.LinkTarget is not null) throw new IOException("Linked path in managed output: " + path);
                if (info is DirectoryInfo) stack.Push(path);
                else if (!allowed.Contains(Path.GetRelativePath(root, path).Replace('\\', '/')))
                    throw new IOException("Untracked file in managed output: " + path);
            }
        }
    }
}

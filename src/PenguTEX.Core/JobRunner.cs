using System.Diagnostics;
using System.Security.Cryptography;
using System.Reflection;

namespace PenguTEX.Core;

public sealed record OutputEntry(string Path, string Source, string SourceHash, string OutputHash,
    int Width, int Height, int DropMips, long Bytes, string Recipe)
{
    public string? SourceContainer { get; init; }
    public int? DeclaredSourceMipCount { get; init; }
    public string? SourceRecordLayout { get; init; }
    public string Operation { get; init; } = "MipCopy";
    public TextureSemantic? Semantic { get; init; }
    public int AlphaCutoff { get; init; } = 128;
    public MaterialHint? Material { get; init; }
    public string Processor { get; init; } = "mip-copy-v1";
}
public sealed record OutputManifest(string Product, int Schema, string Version, DateTimeOffset Created,
    string PolicyHash, IReadOnlyList<OutputEntry> Files, int Unchanged, int Protected, int Unsupported, int Invalid)
{
    public ReferenceReport? Comparison { get; init; }
    public NativeCoverage? Coverage { get; init; }
    public string? MaterialFingerprint { get; init; }
    public bool OriginalsOnly => Files.All(f => f.Operation is "MipCopy" or "Rebuild" or "CompleteMips" or "RepairHeader" or "RecoverMipRecords");
}
public sealed record BuildResult(string Output, int Files, int Reused, long Bytes, double Seconds, OutputManifest Manifest);

public static class JobRunner
{
    public const string ManifestName = "pengutex-manifest.json";
    public const string Product = "PenguTEX";
    public const string RecipeVersion = "mip-copy-v1";
    public static string Version => typeof(JobRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

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

    // Read-only preflight: a first build may target a missing or genuinely empty directory.
    public static void ValidateOutputDirectory(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Choose an output folder.");
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if (new DirectoryInfo(full).LinkTarget is not null) throw new IOException("Output cannot be a symlink.");
        if (File.Exists(full)) throw new IOException("Output is a file. Choose an empty folder: " + full);
        if (!Directory.Exists(full) || !Directory.EnumerateFileSystemEntries(full).Any()) return;
        CheckOwnedTree(full, EnsureOwned(full));
    }

    public static BuildResult Build(Catalog catalog, OptimizationPlan plan, string output, string cache,
        int workers = 2, IProgress<JobProgress>? progress = null, CancellationToken ct = default, ITextureProcessor? processor = null)
    {
        if (plan.Schema != 1 || !plan.CompleteSource || catalog.Diagnostics.Any(d => d.Severity == "Error"))
            throw new InvalidDataException("Resolve source errors before building an output mod.");
        plan.Policy.Validate();
        if (plan.Items.Any(i => i.Decision == Decision.ReferenceCopy) || plan.Comparison?.CarriedCount > 0)
            throw new InvalidDataException("This saved plan contains reference copies. Analyze again using original textures only.");
        if (catalog.Reference is { } baseline && catalog.Assets.Any(a => Within(a.Container, baseline.Folder)))
            throw new InvalidDataException("Reference files cannot be used as original texture sources.");
        if (workers is < 1 or > 32) throw new ArgumentException("Workers must be between 1 and 32.");
        if (plan.MaterialFingerprint is not null)
        {
            if (catalog.Materials is null) throw new IOException("Material evidence is missing. Analyze again.");
            catalog.Materials.VerifyUnchanged(plan.MaterialFingerprint, ct);
        }
        ReferenceComparison.VerifyUnchanged(catalog, plan, ct);
        if (new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(output))).LinkTarget is not null
            || new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(cache))).LinkTarget is not null)
            throw new IOException("Output/cache roots cannot be symbolic links.");
        output = Canonical(output); cache = Canonical(cache);
        if (Within(output, cache) || Within(cache, output)) throw new IOException("Output and cache must be separate directories.");
        foreach (var root in catalog.Roots)
            if (Within(output, root) || Within(root, output) || Within(cache, root) || Within(root, cache))
                throw new IOException("Output/cache must not overlap source roots.");
        foreach (var asset in catalog.Assets.Concat(catalog.Reference?.Assets ?? []))
            if (Within(asset.Container, output) || Within(asset.Container, cache)) throw new IOException("A source resolves inside output/cache.");
        string parent = Path.GetDirectoryName(output) ?? throw new IOException("Output must have a parent directory.");
        Directory.CreateDirectory(parent); Directory.CreateDirectory(cache);
        string identity = JsonFiles.Hash(OperatingSystem.IsWindows() ? output.ToUpperInvariant() : output)[..20];
        using var outputLock = new FileStream(Path.Combine(parent, ".pengutex-" + identity + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string stage = Path.Combine(parent, ".pengutex-stage-" + identity);
        string backup = Path.Combine(parent, ".pengutex-previous-" + identity);
        Recover(output, backup);
        ValidateOutputDirectory(output);
        if (Directory.Exists(stage)) { EnsureOwnedStage(stage); Directory.Delete(stage, true); }
        Directory.CreateDirectory(stage); File.WriteAllText(Path.Combine(stage, ".pengutex-stage"), Product);
        var watch = Stopwatch.StartNew(); string policyHash = JsonFiles.Hash(System.Text.Json.JsonSerializer.Serialize(plan.Policy, JsonFiles.Options));
        var lookup = catalog.Assets.ToDictionary(a => a.Path, StringComparer.Ordinal);
        var changed = plan.Items.Where(i => i.Decision is Decision.Reduce or Decision.Rebuild).ToArray();
        if (changed.Any(i => i.Decision == Decision.Rebuild) && processor is null) throw new InvalidOperationException("This plan requires a CPU texture processor.");
        var entries = new OutputEntry[changed.Length]; int done = 0, reused = 0;
        string store = CacheStore.Folder(cache); Directory.CreateDirectory(store);
        var progressGate = new object();
        progress?.Report(new("Build", 0, changed.Length, "", "Generating textures"));
        Parallel.For(0, changed.Length, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct }, i =>
        {
            var item = changed[i]; ct.ThrowIfCancellationRequested();
            if (!lookup.TryGetValue(item.Path, out var asset) || asset.Container != item.Container)
                throw new IOException("Source winner changed; analyze again: " + item.Path);
            // Only CPU-generated textures are cached. A mip copy costs as much to reuse as to regenerate,
            // so it is produced in one pass and its source hash is known when that pass ends.
            bool cacheable = item.Decision == Decision.Rebuild;
            string processorIdentity = cacheable ? processor!.Identity : RecipeVersion;
            // Everything that determines the output bytes, and nothing else: no policy name or unrelated rule.
            string Recipe(string sourceHash) => JsonFiles.Hash(string.Join('|', sourceHash, processorIdentity, item.Operation, item.DropMips,
                item.Semantic, item.AlphaCutoff, item.TargetWidth, item.TargetHeight, item.Input?.MipCount, item.Input?.DeclaredMipCount));
            string? hash = null; string recipe = "", cached = "", metadata = "";
            string dest = Resolve(stage, item.Path); Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            OutputEntry? entry = null;
            if (cacheable)
            {
                using (var stream = asset.Open()) hash = Hash(stream, ct);
                recipe = Recipe(hash); cached = Path.Combine(store, recipe + ".dds"); metadata = cached + ".json";
            }
            if (cacheable && File.Exists(cached) && File.Exists(metadata))
            {
                try
                {
                    var candidate = JsonFiles.Read<OutputEntry>(metadata);
                    using var check = File.OpenRead(cached); var info = Dds.Inspect(check).Info;
                    if (candidate.Recipe == recipe && candidate.SourceHash == hash && candidate.Processor == processorIdentity
                        && candidate.Operation == item.Operation
                        && candidate.Semantic == item.Semantic && candidate.AlphaCutoff == item.AlphaCutoff && info.Width == item.TargetWidth
                        && info.Height == item.TargetHeight && HashFile(cached, ct) == candidate.OutputHash)
                        entry = candidate with { Path = item.Path, Source = item.Origin, SourceContainer = asset.Container, DeclaredSourceMipCount = item.Input?.DeclaredMipCount, SourceRecordLayout = item.Input?.RecordLayout, Material = item.Material };
                }
                catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or NotSupportedException) { }
            }
            var reusedEntry = entry;
            if (entry is not null)
            {
                File.Copy(cached, dest); Interlocked.Increment(ref reused);
                // Pruning keeps entries a build used recently.
                try { File.SetLastWriteTimeUtc(metadata, DateTime.UtcNow); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            else
            {
                // One pass: the source hash covers exactly the bytes processed, and the output is hashed as written.
                string outputHash;
                using (var source = new HashingReadStream(asset.Open()))
                {
                    using (var target = new HashingWriteStream(File.Create(dest)))
                    {
                        if (item.Decision == Decision.Rebuild) processor!.Process(source, target, item, asset.Length, ct);
                        else Dds.Reduce(source, target, item.DropMips, asset.Length, ct, item.Input);
                        outputHash = target.Complete();
                    }
                    string processed = source.Complete(ct);
                    if (hash is not null && processed != hash) throw new IOException("Source changed during processing: " + item.Path);
                    hash = processed; recipe = Recipe(hash);
                }
                using var verify = File.OpenRead(dest); var info = Dds.Inspect(verify).Info;
                if (info.Width != item.TargetWidth || info.Height != item.TargetHeight || info.PayloadBytes != item.OutputPayload
                    || info.Format != item.Input!.Format || info.Dxgi != item.Input.Dxgi || info.Signed != item.Input.Signed || info.Premultiplied != item.Input.Premultiplied)
                    throw new InvalidDataException("Generated texture does not match the plan: " + item.Path);
                entry = new(item.Path, item.Origin, hash, outputHash, info.Width, info.Height, item.DropMips, verify.Length, recipe)
                { SourceContainer = asset.Container, DeclaredSourceMipCount = item.Input.DeclaredMipCount, SourceRecordLayout = item.Input.RecordLayout, Operation = item.Operation, Semantic = item.Semantic, AlphaCutoff = item.AlphaCutoff, Material = item.Material, Processor = processorIdentity };
            }
            if (cacheable && reusedEntry is null)
            {
                string temporary = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(dest, temporary); TransientIo.Replace(temporary, cached); JsonFiles.Write(metadata, entry); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            entries[i] = entry;
            lock (progressGate) progress?.Report(new("Build", ++done, changed.Length, item.Path, entry.Recipe));
        });
        ct.ThrowIfCancellationRequested();
        var manifest = new OutputManifest(Product, 1, Version, DateTimeOffset.UtcNow,
            policyHash, entries, plan.Items.Count(x => x.Decision == Decision.Keep), plan.Items.Count(x => x.Decision == Decision.Protected),
            plan.Items.Count(x => x.Decision == Decision.Unsupported), plan.Items.Count(x => x.Decision == Decision.Invalid))
        { Comparison = plan.Comparison, Coverage = plan.Coverage, MaterialFingerprint = plan.MaterialFingerprint };
        JsonFiles.Write(Path.Combine(stage, ManifestName), manifest);
        File.WriteAllText(Path.Combine(stage, "meta.ini"), "[General]\ncomments=Generated by PenguTEX. See pengutex-manifest.json.\n");
        Verify(stage, ct, progress); ct.ThrowIfCancellationRequested();
        if (plan.MaterialFingerprint is not null)
        {
            progress?.Report(new("Material recheck", 0, 1, "", "Checking original mesh evidence before publication"));
            catalog.Materials!.VerifyUnchanged(plan.MaterialFingerprint, ct);
            progress?.Report(new("Material recheck", 1, 1, "", "Material sources unchanged"));
        }
        if (catalog.Reference is { } reference)
        {
            progress?.Report(new("Reference recheck", 0, 1, reference.Folder, "Checking that the comparison still applies"));
            ReferenceComparison.VerifyUnchanged(catalog, plan, ct);
            progress?.Report(new("Reference recheck", 1, 1, reference.Folder, "Reference comparison unchanged"));
        }
        progress?.Report(new("Publish", 0, 1, "", "Moving verified textures to the output folder"));
        // Check again after generation so new personal files cannot be swept into a replacement.
        ValidateOutputDirectory(output);
        if (Directory.Exists(output)) Directory.Move(output, backup);
        try
        {
            if (Directory.Exists(backup)) ValidateOutputDirectory(backup);
            Directory.Move(stage, output);
        }
        catch { if (Directory.Exists(backup) && !Directory.Exists(output)) Directory.Move(backup, output); throw; }
        progress?.Report(new("Publish", 1, 1, output, "Output published"));
        if (Directory.Exists(backup))
        {
            progress?.Report(new("Cleanup", 0, 1, "", "Removing the previous PenguTEX output"));
            DeletePreviousOutput(backup);
            progress?.Report(new("Cleanup", 1, 1, "", "Previous output removed"));
        }
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
        string manifestPath = Path.Combine(path, ManifestName);
        if (!File.Exists(manifestPath)) throw new IOException("Output folder has no PenguTEX manifest. Choose an empty folder or a previous PenguTEX output: " + path);
        var manifest = JsonFiles.Read<OutputManifest>(manifestPath);
        if (manifest.Product != Product || manifest.Schema != 1) throw new IOException("Not a recognized PenguTEX output.");
        return manifest;
    }
    private static void Recover(string output, string backup)
    {
        if (!Directory.Exists(backup)) return;
        ValidateOutputDirectory(backup);
        if (!Directory.Exists(output)) Directory.Move(backup, output);
        else { Verify(output); DeletePreviousOutput(backup); }
    }
    private static void DeletePreviousOutput(string path)
    {
        if (!Directory.EnumerateFileSystemEntries(path).Any()) { Directory.Delete(path); return; }
        CheckOwnedTree(path, EnsureOwned(path)); Directory.Delete(path, true);
    }
    public static OutputManifest Verify(string output, CancellationToken ct = default, IProgress<JobProgress>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        var manifest = EnsureOwned(output); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        progress?.Report(new("Verify", 0, manifest.Files.Count, "", "Checking the output tree"));
        CheckOwnedTree(output, manifest, ct);
        int done = 0;
        foreach (var file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested(); if (!seen.Add(file.Path)) throw new InvalidDataException("Duplicate output entry.");
            progress?.Report(new("Verify", done, manifest.Files.Count, file.Path, "Verifying texture"));
            var path = Resolve(output, file.Path); using var stream = File.OpenRead(path); var info = Dds.Inspect(stream).Info;
            if (stream.Length != file.Bytes || info.Width != file.Width || info.Height != file.Height || HashFile(path, ct) != file.OutputHash)
                throw new InvalidDataException("Output verification failed: " + file.Path);
            progress?.Report(new("Verify", ++done, manifest.Files.Count, file.Path, "Texture verified"));
        }
        ct.ThrowIfCancellationRequested();
        return manifest;
    }
    private static void CheckOwnedTree(string root, OutputManifest manifest, CancellationToken ct = default)
    {
        var allowed = manifest.Files.Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
        allowed.UnionWith([ManifestName, "meta.ini", ".pengutex-stage"]);
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var path in Directory.EnumerateFileSystemEntries(stack.Pop()))
            {
                ct.ThrowIfCancellationRequested();
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (info.LinkTarget is not null) throw new IOException("Linked path in managed output: " + path);
                if (info is DirectoryInfo) stack.Push(path);
                else if (!allowed.Contains(Path.GetRelativePath(root, path).Replace('\\', '/')))
                    throw new IOException("Untracked file in managed output: " + path);
            }
        }
    }
}

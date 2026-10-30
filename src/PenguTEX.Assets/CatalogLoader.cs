using PenguTEX.Assets.Materials;
using Mutagen.Bethesda;
using PenguTEX.Assets.Archives;
using PenguTEX.Assets.Paths;
using PenguTEX.Assets.Profiles;
using PenguTEX.Core;

namespace PenguTEX.Assets;

public static class CatalogLoader
{
    public static Catalog Load(SourceOptions options, IProgress<JobProgress>? progress = null, CancellationToken ct = default, bool inspectMaterials = false)
        => Load(options, progress, ct, comparisonOnly: false, inspectMaterials);

    private static Catalog Load(SourceOptions options, IProgress<JobProgress>? progress, CancellationToken ct, bool comparisonOnly, bool inspectMaterials = false)
    {
        if ((options.Folder is null) == (options.Instance is null))
            throw new ArgumentException("Choose exactly one folder or MO2/Fluorine instance.");
        var roots = new List<string>(); var diagnostics = new List<Diagnostic>();
        var priorities = new Dictionary<string, int>(StringComparer.Ordinal);
        string? referenceRoot = options.ReferenceFolder is null ? null : HostPath.Canonical(HostPath.Resolve(options.ReferenceFolder));
        string referencePlacement = "Above folder sources"; int referencePriority = int.MaxValue - 1;
        var loose = new Dictionary<string, Asset>(StringComparer.Ordinal);
        var rootFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Mo2Profile? profile = null;
        IReadOnlySet<string> skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.Folder is not null)
        {
            var root = HostPath.Resolve(options.Folder);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            if (!comparisonOnly && IsOptimizerOutput(root))
                throw new InvalidDataException("This folder is marked as previous optimizer output. Choose original textures as the source and use this folder only as Reference.");
            if (referenceRoot is not null && (referenceRoot == HostPath.Canonical(root)
                || referenceRoot.StartsWith(HostPath.Canonical(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || HostPath.Canonical(root).StartsWith(referenceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                throw new ArgumentException("Reference and original source folders must not overlap.");
            roots.Add(root);
        }
        else
        {
            var instance = Mo2Instance.Open(HostPath.Resolve(options.Instance!), new(options.Game, options.Mods,
                options.Profiles, options.Overwrite, options.WinePrefix));
            profile = instance.OpenProfile(options.Profile); skipDirs = instance.SkipDirectories;
            options = options with { Profile = profile.Name };
            var ranks = profile.AllModsLowToHigh.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index + 1, StringComparer.OrdinalIgnoreCase);
            if (referenceRoot is not null)
            {
                string? replace = options.ReplaceMod;
                if (replace is null && Path.GetDirectoryName(referenceRoot) == HostPath.Canonical(instance.ModsFolder)) replace = Path.GetFileName(referenceRoot);
                if (replace is null || !ranks.TryGetValue(replace, out referencePriority))
                    throw new ArgumentException("For a profile comparison, select the installed reference mod folder or specify ReplaceMod / --replace-mod with its profile mod name.");
                referencePlacement = replace; options = options with { ReplaceMod = replace };
            }
            var baseData = options.Data is null ? instance.GameDataFolder : HostPath.Resolve(options.Data);
            if (!Directory.Exists(baseData)) throw new DirectoryNotFoundException("Base Data folder is missing: " + baseData);
            if (OperatingSystem.IsLinux() && IsFluorineMount(baseData, File.ReadLines("/proc/self/mountinfo")))
                throw new IOException("The game Data folder is a merged Fluorine filesystem. Stop its virtual filesystem before scanning a fresh baseline, or select an unmounted original Data folder with --data. A merged view may contain previous optimizer output and cannot reconstruct hidden originals.");
            roots.Add(baseData);
            foreach (var mod in profile.EnabledModsLowToHigh)
            {
                ct.ThrowIfCancellationRequested();
                if (options.ExcludeMods?.Contains(mod, StringComparer.OrdinalIgnoreCase) == true)
                { diagnostics.Add(new("Info", "Excluded requested mod: " + mod)); continue; }
                var path = GamePath.ResolveExisting(instance.ModsFolder, mod);
                if (path is null || !Directory.Exists(path)) { diagnostics.Add(new("Error", "Enabled mod folder is missing: " + mod)); continue; }
                if (referenceRoot is not null && (mod.Equals(referencePlacement, StringComparison.OrdinalIgnoreCase) || HostPath.Canonical(path) == referenceRoot))
                { diagnostics.Add(new("Info", "Reference output excluded from original inputs: " + mod)); continue; }
                if (IsOptimizerOutput(path)) { diagnostics.Add(new("Info", "Excluded prior optimizer output: " + mod)); continue; }
                roots.Add(path); priorities[path] = ranks[mod];
            }
            if (Directory.Exists(instance.OverwriteFolder)) { roots.Add(instance.OverwriteFolder); priorities[instance.OverwriteFolder] = int.MaxValue; }
        }
        for (int i = 0; i < roots.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); string root = roots[i]; string origin = Path.GetFileName(root);
            progress?.Report(new("Resolve", i, roots.Count, origin, "Indexing source layers"));
            try
            {
                var perRoot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.EnumerateFiles(root))
                {
                    if (f.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!perRoot.TryAdd(Path.GetFileName(f), f)) diagnostics.Add(new("Error", "Case collision in " + origin + ": " + Path.GetFileName(f)));
                }
                foreach (var (key, value) in perRoot) rootFiles[key] = value;
                foreach (string kind in inspectMaterials ? new[] { "textures", "meshes" } : new[] { "textures" })
                {
                    string? contentRoot = GamePath.ResolveExisting(root, kind);
                    if (profile is null && kind == "textures" && contentRoot is null) contentRoot = root;
                    if (contentRoot is null || !Directory.Exists(contentRoot)) continue;
                    var layerFiles = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var file in Walk(contentRoot, skipDirs, diagnostics, ct))
                    {
                        if (!file.EndsWith(kind == "textures" ? ".dds" : ".nif", StringComparison.OrdinalIgnoreCase)) continue;
                        string key = kind + "/" + Path.GetRelativePath(contentRoot, file).Replace('\\', '/').ToLowerInvariant();
                        GamePath.ValidateRelative(key);
                        if (!layerFiles.TryAdd(key, file)) diagnostics.Add(new("Error", "Case collision in " + origin + ": " + key));
                    }
                    foreach (var (key, file) in layerFiles)
                    {
                        var actual = file;
                        loose[key] = new(key, origin, actual, new FileInfo(actual).Length,
                            () => new FileStream(actual, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan))
                        { Priority = priorities.GetValueOrDefault(root) };
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { diagnostics.Add(new("Error", $"Cannot index {origin}: {e.Message}")); }
        }
        var assets = new Dictionary<string, Asset>(StringComparer.Ordinal);
        if (profile is not null)
        {
            var iniArchives = profile.IniArchives(options.Ini);
            foreach (var missing in iniArchives.Where(n => !rootFiles.ContainsKey(n)))
                diagnostics.Add(missing.Equals("Skyrim - Patch.bsa", StringComparison.OrdinalIgnoreCase)
                    ? new("Warning", "Optional legacy patch archive is absent: " + missing)
                    : new("Error", "Active INI archive is missing; restore it or correct the archive list: " + missing));
            var archives = SkyrimArchives.Select(GameRelease.SkyrimSE, rootFiles.Values, profile.EnabledPlugins, iniArchives);
            for (int i = 0; i < archives.Count; i++)
            {
                ct.ThrowIfCancellationRequested(); var archive = archives[i];
                progress?.Report(new("Archives", i, archives.Count, Path.GetFileName(archive), "Indexing loaded archives"));
                try
                {
                    var archiveMembers = BoundedBsa.Read(archive, diagnostics);
                    var members = new Dictionary<string, Asset>(StringComparer.Ordinal);
                    foreach (var entry in archiveMembers)
                    {
                        ct.ThrowIfCancellationRequested(); var path = entry.Path.Replace('\\', '/').ToLowerInvariant();
                        if (!(path.StartsWith("textures/") && path.EndsWith(".dds"))
                            && !(inspectMaterials && path.StartsWith("meshes/") && path.EndsWith(".nif"))) continue;
                        GamePath.ValidateRelative(path); var file = entry;
                        if (!members.TryAdd(path, new(path, Path.GetFileName(archive), archive, file.Size, () => file.Open()) { Priority = -1 }))
                            throw new InvalidDataException("Duplicate archive member: " + path);
                    }
                    foreach (var (key, asset) in members) assets[key] = asset;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                { diagnostics.Add(new("Error", $"Cannot index archive {Path.GetFileName(archive)}: {e.Message}")); }
            }
        }
        foreach (var (key, asset) in loose) assets[key] = asset;
        ReferenceCatalog? reference = null;
        if (referenceRoot is not null)
        {
            var referenceFiles = Load(new(Folder: referenceRoot), progress, ct, comparisonOnly: true);
            diagnostics.AddRange(referenceFiles.Diagnostics.Select(d => d with { Message = "Reference: " + d.Message }));
            if (referenceFiles.Assets.Count == 0) diagnostics.Add(new("Error", "Reference folder contains no loose DDS textures."));
            reference = new(referenceRoot, referencePlacement, referencePriority, referenceFiles.Assets)
            {
                Refresh = token =>
                {
                    var refreshed = Load(new(Folder: referenceRoot), null, token, comparisonOnly: true);
                    if (refreshed.Diagnostics.Any(d => d.Severity == "Error")) throw new IOException("Reference output cannot be read consistently. Analyze again.");
                    return new(referenceRoot, referencePlacement, referencePriority, refreshed.Assets);
                }
            };
            roots.Add(referenceRoot); // Protect comparison inputs from output/cache overlap too.
        }
        MaterialCatalog? materials = null;
        if (inspectMaterials)
        {
            materials = MaterialReader.Read(assets.Values.Where(a => a.Path.StartsWith("meshes/")).ToArray(), diagnostics, progress, ct);
            var freshOptions = options with { ReferenceFolder = null, ReplaceMod = null };
            materials = materials with { RefreshFingerprint = token =>
            {
                var refreshed = Load(freshOptions, progress, token, false, true);
                if (refreshed.Diagnostics.Any(d => d.Severity == "Error"))
                    throw new IOException("Original source inventory became incomplete during material recheck. Analyze again.");
                return refreshed.Materials!.Fingerprint;
            } };
        }
        return new(options, roots, assets.Values.Where(a => a.Path.StartsWith("textures/")).OrderBy(a => a.Path, StringComparer.Ordinal).ToArray(), diagnostics)
        { Reference = reference, Materials = materials };
    }

    private static bool IsOptimizerOutput(string folder) => GamePath.ResolveExisting(folder, JobRunner.ManifestName) is not null
        || GamePath.ResolveExisting(folder, "VRAMrOutput.tmp") is not null;

    public static bool IsFluorineMount(string data, IEnumerable<string> mountInfo)
    {
        string target = Path.TrimEndingDirectorySeparator(HostPath.Canonical(data));
        foreach (string line in mountInfo)
        {
            string[] fields = line.Split(' '); int separator = Array.IndexOf(fields, "-");
            if (fields.Length < 6 || separator < 0 || fields.Length <= separator + 2) continue;
            string mount = fields[4].Replace("\\040", " ").Replace("\\011", "\t").Replace("\\134", "\\");
            mount = Path.TrimEndingDirectorySeparator(mount);
            if ((mount == target || target.StartsWith(mount + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                && fields[separator + 2] == "mo2linux") return true;
        }
        return false;
    }

    private static IEnumerable<string> Walk(string root, IReadOnlySet<string> skip, List<Diagnostic> diagnostics, CancellationToken ct)
    {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested(); var dir = stack.Pop();
            foreach (var f in Directory.EnumerateFiles(dir)) if (!f.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)) yield return f;
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                if (skip.Contains(Path.GetFileName(child)) || child.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)) continue;
                if (new DirectoryInfo(child).LinkTarget is not null)
                { diagnostics.Add(new("Error", "Linked source directory needs an explicit physical source: " + Path.GetFileName(child))); continue; }
                stack.Push(child);
            }
        }
    }
}

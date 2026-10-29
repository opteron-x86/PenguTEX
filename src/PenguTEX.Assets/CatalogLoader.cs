using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using PenguTEX.Assets.Paths;
using PenguTEX.Assets.Profiles;
using PenguTEX.Core;

namespace PenguTEX.Assets;

public static class CatalogLoader
{
    public static Catalog Load(SourceOptions options, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        if ((options.Folder is null) == (options.Instance is null))
            throw new ArgumentException("Choose exactly one folder or MO2/Fluorine instance.");
        var roots = new List<string>(); var diagnostics = new List<Diagnostic>();
        var loose = new Dictionary<string, Asset>(StringComparer.Ordinal);
        var rootFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Mo2Profile? profile = null;
        IReadOnlySet<string> skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.Folder is not null)
        {
            var root = HostPath.Resolve(options.Folder);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            roots.Add(root);
        }
        else
        {
            var instance = Mo2Instance.Open(HostPath.Resolve(options.Instance!), new(options.Game, options.Mods,
                options.Profiles, options.Overwrite, options.WinePrefix));
            profile = instance.OpenProfile(options.Profile); skipDirs = instance.SkipDirectories;
            options = options with { Profile = profile.Name };
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
                if (IsOptimizerOutput(path)) { diagnostics.Add(new("Info", "Excluded prior optimizer output: " + mod)); continue; }
                roots.Add(path);
            }
            if (Directory.Exists(instance.OverwriteFolder)) roots.Add(instance.OverwriteFolder);
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
                string? textureRoot = GamePath.ResolveExisting(root, "textures"); string prefix = "textures/";
                if (profile is null && textureRoot is null) textureRoot = root;
                if (textureRoot is null || !Directory.Exists(textureRoot)) continue;
                var layerFiles = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var file in Walk(textureRoot, skipDirs, diagnostics, ct))
                {
                    if (!file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) continue;
                    string key = prefix + Path.GetRelativePath(textureRoot, file).Replace('\\', '/').ToLowerInvariant();
                    GamePath.ValidateRelative(key);
                    if (!layerFiles.TryAdd(key, file)) diagnostics.Add(new("Error", "Case collision in " + origin + ": " + key));
                }
                foreach (var (key, file) in layerFiles)
                {
                    var actual = file;
                    loose[key] = new(key, origin, actual, new FileInfo(actual).Length,
                        () => new FileStream(actual, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan));
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
                diagnostics.Add(new("Warning", "INI archive not found (some default entries are optional): " + missing));
            var archives = SkyrimArchives.Select(GameRelease.SkyrimSE, rootFiles.Values, profile.EnabledPlugins, iniArchives);
            for (int i = 0; i < archives.Count; i++)
            {
                ct.ThrowIfCancellationRequested(); var archive = archives[i];
                progress?.Report(new("Archives", i, archives.Count, Path.GetFileName(archive), "Indexing loaded archives"));
                try
                {
                    var reader = Archive.CreateReader(GameRelease.SkyrimSE, archive);
                    var members = new Dictionary<string, Asset>(StringComparer.Ordinal);
                    foreach (var entry in reader.Files)
                    {
                        ct.ThrowIfCancellationRequested(); var path = entry.Path.Replace('\\', '/').ToLowerInvariant();
                        if (!path.StartsWith("textures/") || !path.EndsWith(".dds")) continue;
                        GamePath.ValidateRelative(path); var file = entry;
                        if (!members.TryAdd(path, new(path, Path.GetFileName(archive), archive, file.Size, () => file.AsStream())))
                            throw new InvalidDataException("Duplicate archive member: " + path);
                    }
                    foreach (var (key, asset) in members) assets[key] = asset;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                { diagnostics.Add(new("Error", $"Cannot index archive {Path.GetFileName(archive)}: {e.Message}")); }
            }
        }
        foreach (var (key, asset) in loose) assets[key] = asset;
        return new(options, roots, assets.Values.OrderBy(a => a.Path, StringComparer.Ordinal).ToArray(), diagnostics);
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
            if (Path.TrimEndingDirectorySeparator(mount) == target && fields[separator + 2] == "mo2linux") return true;
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

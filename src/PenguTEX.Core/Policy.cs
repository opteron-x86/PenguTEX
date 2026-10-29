using System.Text.RegularExpressions;

namespace PenguTEX.Core;

public sealed record PathRule(string Pattern, int? Maximum = null, bool Protect = false);
public sealed record Policy
{
    public int Schema { get; init; } = 1;
    public string Name { get; init; } = "Balanced";
    public int Maximum { get; init; } = 2048;
    public int NormalMaximum { get; init; } = 2048;
    public int MaterialMaximum { get; init; } = 1024;
    public bool ProtectLargeSurfaces { get; init; } = true;
    public bool ProtectCharacters { get; init; } = true;
    public PathRule[] Rules { get; init; } = [];

    public void Validate()
    {
        if (Schema != 1) throw new InvalidDataException("Unsupported policy schema.");
        foreach (int value in new[] { Maximum, NormalMaximum, MaterialMaximum }.Concat(Rules.Where(r => r.Maximum.HasValue).Select(r => r.Maximum!.Value)))
            if (value < 4 || value > 16384 || (value & (value - 1)) != 0)
                throw new ArgumentException("Maximum dimensions must be powers of two from 4 to 16384.");
        foreach (var rule in Rules) if (string.IsNullOrWhiteSpace(rule.Pattern)) throw new ArgumentException("Empty path rule.");
    }
    public static Policy Preset(string name) => name.ToLowerInvariant() switch
    {
        "quality" => new() { Name = "Quality", Maximum = 4096, NormalMaximum = 2048, MaterialMaximum = 2048 },
        "balanced" => new(),
        "compact" => new() { Name = "Compact", Maximum = 1024, NormalMaximum = 1024, MaterialMaximum = 512 },
        _ => throw new ArgumentException("Preset must be Quality, Balanced or Compact."),
    };
    public static bool Matches(string path, string pattern) => Regex.IsMatch(path.Replace('\\', '/'),
        "^" + Regex.Escape(pattern.Replace('\\', '/')).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public (string Category, int Maximum, string? Protection) Classify(string path)
    {
        string p = path.Replace('\\', '/').ToLowerInvariant(), file = System.IO.Path.GetFileNameWithoutExtension(p);
        string category = file.EndsWith("_n") || file.EndsWith("_msn") || file.EndsWith("_normal") ? "Normal"
            : file.EndsWith("_p") || file.EndsWith("_m") || file.EndsWith("_rmaos") || file.EndsWith("_s") ? "Material" : "Color";
        int maximum = category == "Normal" ? NormalMaximum : category == "Material" ? MaterialMaximum : Maximum;
        string? protect = null;
        if (p.StartsWith("textures/interface/") || p.Contains("/terrain/") || p.Contains("/lod/") || p.Contains("/dyndolod/") || p.Contains("/lodgen/") || p.Contains("/shaders/"))
            protect = "Generated LOD, interface or shader resource.";
        else if (ProtectCharacters && (p.Contains("/actors/character/") || p.Contains("/facegen/"))) protect = "Character and face protection.";
        else if (ProtectLargeSurfaces && (p.Contains("/landscape/") || p.Contains("/actors/dragon") || p.Contains("/actors/alduin/"))) protect = "Large surface protection.";
        foreach (var rule in Rules)
            if (Matches(p, rule.Pattern))
            {
                if (rule.Protect) protect = "Explicit protection rule: " + rule.Pattern;
                else if (rule.Maximum.HasValue) { maximum = rule.Maximum.Value; protect = null; }
            }
        return (category, maximum, protect);
    }
}

public static class Planner
{
    public static OptimizationPlan Analyze(Catalog catalog, Policy policy, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        policy.Validate(); var items = new List<PlanItem>();
        foreach (var asset in catalog.Assets.OrderBy(a => a.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested(); var (category, maximum, protection) = policy.Classify(asset.Path);
            try
            {
                using var stream = asset.Open(); var (info, _) = Dds.Inspect(stream, asset.Length);
                int drop = info.DropFor(maximum); int width = Math.Max(1, info.Width >> drop), height = Math.Max(1, info.Height >> drop);
                Decision decision; string reason;
                if (protection is not null) { decision = Decision.Protected; reason = protection; drop = 0; }
                else if (Math.Max(width, height) > maximum) { decision = Decision.Keep; reason = "No existing mip reaches the selected maximum; use explicit mip rebuilding when available."; drop = 0; }
                else if (drop > 0) { decision = Decision.Reduce; reason = $"Retain existing mip {drop} and its smaller levels; {category.ToLowerInvariant()} cap {maximum}."; }
                else { decision = Decision.Keep; reason = "Already within the selected maximum."; }
                items.Add(new(asset.Path, asset.Origin, asset.Container, category, decision, reason, info, drop,
                    Math.Max(1, info.Width >> drop), Math.Max(1, info.Height >> drop), info.PayloadBytes - info.RemovedBytes(drop)));
            }
            catch (NotSupportedException e) { items.Add(new(asset.Path, asset.Origin, asset.Container, category, Decision.Unsupported, e.Message, null, 0, 0, 0, 0)); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException or ArgumentException)
            { items.Add(new(asset.Path, asset.Origin, asset.Container, category, Decision.Invalid, e.Message, null, 0, 0, 0, 0)); }
            if (items.Count % 100 == 0 || items.Count == catalog.Assets.Count)
                progress?.Report(new("Analyze", items.Count, catalog.Assets.Count, asset.Path, "Inspecting winning textures"));
        }
        return new(1, catalog.Source, policy, DateTimeOffset.UtcNow, items, catalog.Diagnostics);
    }
}

using System.Text.RegularExpressions;

namespace PenguTEX.Core;

public enum TextureSemantic { SrgbColor, LinearColor, LinearData, NormalRgb, NormalRg, OpacitySrgb }
public sealed record PathRule(string Pattern, int? Maximum = null, bool Protect = false,
    bool RebuildMips = false, TextureSemantic? Semantic = null);
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
        foreach (var rule in Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) throw new ArgumentException("Empty path rule.");
            if (rule.RebuildMips && (rule.Semantic is null || !Enum.IsDefined(rule.Semantic.Value)))
                throw new ArgumentException("Mip rebuilding requires an explicit, supported Semantic in each rebuild rule.");
        }
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

    public PathRule? ProcessingRule(string path) => Rules.LastOrDefault(r => Matches(path, r.Pattern));

    public static string? RebuildRestriction(DdsInfo info, TextureSemantic semantic)
    {
        if (info.Format == "BC6H") return "HDR BC6H currently supports existing-mip copying only; rebuilding requires an HDR filter.";
        if (!info.IsBlockCompressed && !info.HasByteChannels) return "This uncompressed layout supports existing-mip copying only; rebuilding requires a precision-preserving decoder.";
        if (!info.IsBlockCompressed) return "Uncompressed mip rebuilding is not available yet; existing-mip copying is supported.";
        if (info.Signed || info.Premultiplied) return "CPU rebuilding does not support signed channels or premultiplied alpha; use existing-mip copying.";
        if ((long)info.Width * info.Height > 4096L * 4096) return "CPU rebuilding is limited to 16 million source pixels to bound decoded memory.";
        if (info.Srgb && semantic is not (TextureSemantic.SrgbColor or TextureSemantic.OpacitySrgb)) return "sRGB headers require an sRGB color semantic; changing the format is not supported.";
        if (semantic == TextureSemantic.NormalRg && info.Format != "BC5") return "NormalRg requires unsigned BC5; swizzled normal layouts are not inferred.";
        if (semantic != TextureSemantic.LinearData && semantic != TextureSemantic.NormalRg && info.Format is "BC4" or "BC5") return "Single/two-channel textures require LinearData or BC5 NormalRg semantics.";
        return null;
    }

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
                Decision decision; string reason; var rebuild = policy.ProcessingRule(asset.Path);
                if (protection is not null) { decision = Decision.Protected; reason = protection; drop = 0; }
                else if (rebuild?.RebuildMips == true)
                {
                    if (Policy.RebuildRestriction(info, rebuild.Semantic!.Value) is { } restriction)
                    { decision = Decision.Unsupported; reason = restriction; drop = 0; }
                    else
                    {
                        drop = 0;
                        while (Math.Max(Math.Max(1, info.Width >> drop), Math.Max(1, info.Height >> drop)) > maximum) drop++;
                        width = Math.Max(1, info.Width >> drop); height = Math.Max(1, info.Height >> drop);
                        var target = info with { Width = width, Height = height, MipCount = 1 + (int)Math.Log2(Math.Max(width, height)) };
                        items.Add(new(asset.Path, asset.Origin, asset.Container, category, Decision.Rebuild,
                            $"Explicit CPU rebuild from the top mip; {rebuild.Semantic}; original {info.Format} format retained.", info, drop, width, height,
                            Enumerable.Range(0, target.MipCount).Sum(target.MipBytes)) { Semantic = rebuild.Semantic });
                        if (items.Count % 100 == 0 || items.Count == catalog.Assets.Count)
                            progress?.Report(new("Analyze", items.Count, catalog.Assets.Count, asset.Path, "Planning explicit mip rebuilds"));
                        continue;
                    }
                }
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

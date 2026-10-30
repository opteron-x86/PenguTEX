using System.Text.RegularExpressions;

namespace PenguTEX.Core;

public enum TextureSemantic { SrgbColor, LinearColor, LinearData, NormalRgb, NormalRg, OpacitySrgb }
public sealed record PathRule(string Pattern, int? Maximum = null, bool Protect = false,
    bool RebuildMips = false, TextureSemantic? Semantic = null, bool CompleteMipChain = false);
public sealed record Policy
{
    public const long MaximumRebuildPixels = 8192L * 8192;
    public int Schema { get; init; } = 1;
    public string Name { get; init; } = "Balanced";
    public int Maximum { get; init; } = 2048;
    public int NormalMaximum { get; init; } = 1024;
    public int MaterialMaximum { get; init; } = 512;
    public int? HeightMaximum { get; init; }
    public bool ProtectLargeSurfaces { get; init; } = true;
    public bool ProtectCharacters { get; init; } = true;
    public bool FitReference { get; init; }
    public bool FitProtectedReference { get; init; }
    // Retained only to reject legacy policies explicitly, rather than ignore their intent.
    public bool CarryReference { get; init; }
    public PathRule[] Rules { get; init; } = [];

    public void Validate()
    {
        if (CarryReference) throw new ArgumentException("Reference copying is no longer supported. Disable CarryReference and rebuild from original textures.");
        if (Schema != 1) throw new InvalidDataException("Unsupported policy schema.");
        if (FitProtectedReference && !FitReference) throw new ArgumentException("Protected reference fitting requires FitReference.");
        foreach (int value in new[] { Maximum, NormalMaximum, MaterialMaximum, HeightMaximum ?? MaterialMaximum }.Concat(Rules.Where(r => r.Maximum.HasValue).Select(r => r.Maximum!.Value)))
            if (value < 4 || value > 16384 || (value & (value - 1)) != 0)
                throw new ArgumentException("Maximum dimensions must be powers of two from 4 to 16384.");
        foreach (var rule in Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) throw new ArgumentException("Empty path rule.");
            if (rule.RebuildMips && rule.CompleteMipChain) throw new ArgumentException("Choose RebuildMips or CompleteMipChain, not both in one rule.");
            if ((rule.RebuildMips || rule.CompleteMipChain) && (rule.Semantic is null || !Enum.IsDefined(rule.Semantic.Value)))
                throw new ArgumentException("Mip rebuilding/completion requires an explicit, supported Semantic in each processing rule.");
        }
    }
    public static Policy Preset(string name) => name.ToLowerInvariant() switch
    {
        "quality" => new() { Name = "Quality", Maximum = 2048, NormalMaximum = 1024, MaterialMaximum = 1024, HeightMaximum = 1024 },
        "balanced" => new(),
        "compact" => new() { Name = "Compact", Maximum = 1024, NormalMaximum = 512, MaterialMaximum = 512, HeightMaximum = 256 },
        _ => throw new ArgumentException("Preset must be Quality, Balanced or Compact."),
    };
    public static bool Matches(string path, string pattern) => Regex.IsMatch(path.Replace('\\', '/'),
        "^" + Regex.Escape(pattern.Replace('\\', '/')).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public PathRule? ProcessingRule(string path) => Rules.LastOrDefault(r => Matches(path, r.Pattern));

    public static string? RebuildRestriction(DdsInfo info, TextureSemantic semantic, bool preserveExistingMips = false)
    {
        if (info.Format == "BC6H") return "HDR BC6H currently supports existing-mip copying only; rebuilding requires an HDR filter.";
        if (!info.IsBlockCompressed && !info.HasByteChannels) return "This uncompressed layout supports existing-mip copying only; rebuilding requires a precision-preserving decoder.";
        if (info.Signed || info.Premultiplied) return "CPU rebuilding does not support signed channels or premultiplied alpha; use existing-mip copying.";
        int level = preserveExistingMips ? info.MipCount - 1 : 0;
        if ((long)Math.Max(1, info.Width >> level) * Math.Max(1, info.Height >> level) > MaximumRebuildPixels) return "CPU rebuilding is limited to 64 million source pixels, with one decoded texture at a time; use existing-mip copying for larger inputs.";
        if (info.Srgb && semantic is not (TextureSemantic.SrgbColor or TextureSemantic.OpacitySrgb)) return "sRGB headers require an sRGB color semantic; changing the format is not supported.";
        bool rawRg = !info.IsBlockCompressed && info.RedMask != 0 && info.GreenMask != 0 && info.BlueMask == 0 && !info.Luminance;
        if (semantic == TextureSemantic.NormalRg && info.Format != "BC5" && !rawRg) return "NormalRg requires unsigned BC5 or raw RG channels; swizzled normal layouts are not inferred.";
        if (semantic != TextureSemantic.LinearData && semantic != TextureSemantic.NormalRg && info.Format is "BC4" or "BC5") return "Single/two-channel textures require LinearData or BC5 NormalRg semantics.";
        if (!info.IsBlockCompressed && semantic is not (TextureSemantic.LinearData or TextureSemantic.NormalRg))
        {
            bool rgb = info.RedMask != 0 && info.GreenMask != 0 && info.BlueMask != 0;
            if (!rgb && (!info.Luminance || semantic == TextureSemantic.NormalRgb)) return "This raw channel layout requires LinearData or compatible NormalRg semantics.";
            if (semantic == TextureSemantic.OpacitySrgb && info.AlphaMask == 0) return "OpacitySrgb requires a stored alpha channel.";
        }
        return null;
    }

    public (string Category, int Maximum, string? Protection) Classify(string path)
    {
        string p = path.Replace('\\', '/').ToLowerInvariant(), file = System.IO.Path.GetFileNameWithoutExtension(p);
        string category = file.EndsWith("_n") || file.EndsWith("_msn") || file.EndsWith("_normal") ? "Normal"
            : file.EndsWith("_p") ? "Height"
            : file.EndsWith("_m") || file.EndsWith("_rmaos") || file.EndsWith("_s") || file.EndsWith("_g") || file.EndsWith("_em") ? "Material" : "Color";
        int maximum = category == "Normal" ? NormalMaximum : category == "Height" ? HeightMaximum ?? MaterialMaximum : category == "Material" ? MaterialMaximum : Maximum;
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
        if (policy.FitReference && catalog.Reference is null) throw new ArgumentException("Choose a reference output before enabling reference fitting.");
        var references = ReferenceComparison.Read(catalog.Reference, progress, ct);
        foreach (var asset in catalog.Assets.OrderBy(a => a.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested(); var (category, maximum, protection) = policy.Classify(asset.Path);
            references.TryGetValue(asset.Path, out var reference);
            bool higherPriority = catalog.Reference is { } placement && asset.Priority > placement.Priority;
            if (policy.FitProtectedReference && reference?.Info is not null && protection is "Character and face protection." or "Large surface protection.")
                protection = null;
            try
            {
                using var stream = asset.Open(); var (info, _) = Dds.Inspect(stream, asset.Length);
                int drop = info.DropFor(maximum); int width = Math.Max(1, info.Width >> drop), height = Math.Max(1, info.Height >> drop);
                if (policy.FitReference && reference?.Info is { } previous)
                    while (drop < info.MipCount - 1 && !ReferenceComparison.Fits(info, drop, false, previous)) drop++;
                width = Math.Max(1, info.Width >> drop); height = Math.Max(1, info.Height >> drop);
                Decision decision; string reason; var rebuild = policy.ProcessingRule(asset.Path);
                if (higherPriority) { decision = Decision.Keep; reason = "Source is above the selected output priority and will remain unchanged."; drop = 0; }
                else if (protection is not null) { decision = Decision.Protected; reason = protection; drop = 0; }
                else if (rebuild is { RebuildMips: true } or { CompleteMipChain: true })
                {
                    bool complete = rebuild.CompleteMipChain;
                    int fullCount = 1 + (int)Math.Log2(Math.Max(info.Width, info.Height));
                    if (complete && info.MipCount == fullCount)
                    {
                        decision = drop > 0 ? Decision.Reduce : Decision.Keep;
                        reason = "Original mip chain is complete; retain its existing levels without recompression.";
                    }
                    else if (Policy.RebuildRestriction(info, rebuild.Semantic!.Value, complete) is { } restriction)
                    { decision = Decision.Unsupported; reason = restriction; drop = 0; }
                    else
                    {
                        drop = 0;
                        while (Math.Max(Math.Max(1, info.Width >> drop), Math.Max(1, info.Height >> drop)) > maximum) drop++;
                        if (policy.FitReference && reference?.Info is { } previousRebuild)
                            while (Math.Max(info.Width >> drop, info.Height >> drop) > 1 && !ReferenceComparison.Fits(info, drop, true, previousRebuild)) drop++;
                        width = Math.Max(1, info.Width >> drop); height = Math.Max(1, info.Height >> drop);
                        var target = info with { Width = width, Height = height, MipCount = 1 + (int)Math.Log2(Math.Max(width, height)) };
                        items.Add(new(asset.Path, asset.Origin, asset.Container, category, Decision.Rebuild,
                            complete ? $"Complete original mip chain; retain available levels byte-for-byte and generate missing levels with {rebuild.Semantic}; original {info.Format} format retained."
                                : $"Explicit CPU rebuild from the top mip; {rebuild.Semantic}; original {info.Format} format retained.", info, drop, width, height,
                            Enumerable.Range(0, target.MipCount).Sum(target.MipBytes)) { Semantic = rebuild.Semantic, PreserveExistingMips = complete });
                        if (items.Count % 100 == 0 || items.Count == catalog.Assets.Count)
                            progress?.Report(new("Analyze", items.Count, catalog.Assets.Count, asset.Path, "Planning explicit mip rebuilds"));
                        continue;
                    }
                }
                else if (policy.FitReference && reference?.Info is { } previousCopy && !ReferenceComparison.Fits(info, drop, false, previousCopy))
                { decision = Decision.Keep; reason = "No existing mip fits the reference dimensions, payload and mip coverage; an explicit rebuild from the original is needed."; drop = 0; }
                else if (Math.Max(width, height) > maximum) { decision = Decision.Keep; reason = "No existing mip reaches the selected maximum; use explicit mip rebuilding when available."; drop = 0; }
                else if (drop > 0) { decision = Decision.Reduce; reason = $"Retain existing mip {drop} and its smaller levels; {category.ToLowerInvariant()} cap {maximum}." + (policy.FitReference && reference?.Info is not null ? " Also fits reference dimensions and payload without changing format." : ""); }
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
        return new(1, catalog.Source, policy, DateTimeOffset.UtcNow, items, catalog.Diagnostics)
        { Comparison = ReferenceComparison.Compare(catalog, items, references) };
    }
}

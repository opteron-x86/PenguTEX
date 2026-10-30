using System.Text.Json;

namespace PenguTEX.Core;

public sealed record ReferenceTexture(string Path, long FileBytes, DdsInfo? Info, string? Problem);
public sealed record ReferenceDifference(string Path, string Source, string Status, string Reason,
    DdsInfo? Previous, DdsInfo? Proposed, long? PreviousPayload, long? ProposedPayload)
{
    public long? ChangeBytes => PreviousPayload.HasValue && ProposedPayload.HasValue ? ProposedPayload - PreviousPayload : null;
}
public sealed record ReferenceReport(string Folder, string Placement, string Fingerprint, IReadOnlyList<ReferenceDifference> Items)
{
    public int LargerCount => Items.Count(i => i.Status == "Larger");
    public int UnknownCount => Items.Count(i => i.Status is "UnknownReference" or "UnknownSource" or "MissingSource");
    public int CarriedCount => Items.Count(i => i.Status == "ReferenceRetained");
    public int IncompleteMipCount => Items.Count(i => i.Status == "IncompleteMips");
    public long KnownPayloadChange => Items.Sum(i => i.ChangeBytes ?? 0);
    public bool HasUnresolved => LargerCount != 0 || UnknownCount != 0 || IncompleteMipCount != 0;
    public string Assessment => HasUnresolved ? "Replacement has larger, incomplete or unassessed textures. Keep the prior output until reviewed."
        : $"No larger dimensions/payload or reduced mip coverage found in this comparison. {CarriedCount:N0} reference files carried unchanged; these are not native optimizations. In-game appearance and performance still require validation.";
}

public static class ReferenceComparison
{
    public static void CarryForward(Catalog catalog, List<PlanItem> items, IReadOnlyDictionary<string, ReferenceTexture> references, Policy policy)
    {
        var report = Compare(catalog, items, references)!;
        var referenceAssets = catalog.Reference!.Assets.ToDictionary(a => a.Path, StringComparer.Ordinal);
        var positions = items.Select((item, index) => (item.Path, index)).ToDictionary(x => x.Path, x => x.index, StringComparer.Ordinal);
        foreach (var gap in report.Items.Where(i => i.Status is "Larger" or "IncompleteMips" or "UnknownSource" or "MissingSource"))
        {
            if (references[gap.Path].Info is not { } info) continue;
            bool exists = positions.TryGetValue(gap.Path, out int position);
            // Copying an old override must not silently bypass a deliberate protection.
            var protection = policy.Classify(gap.Path).Protection;
            bool softFit = policy.FitProtectedReference && protection is "Character and face protection." or "Large surface protection.";
            if (exists && items[position].Decision == Decision.Protected || protection is not null && !softFit) continue;
            var asset = referenceAssets[gap.Path];
            var copy = new PlanItem(gap.Path, "Reference: " + catalog.Reference.Placement, asset.Container, "Reference", Decision.ReferenceCopy,
                $"Carry previous file byte-for-byte ({gap.Status}): {gap.Reason}", info, 0, info.Width, info.Height, info.PayloadBytes);
            if (exists) items[position] = copy; else items.Add(copy);
        }
        items.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
    }
    public static IReadOnlyDictionary<string, ReferenceTexture> Read(ReferenceCatalog? reference,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var result = new Dictionary<string, ReferenceTexture>(StringComparer.Ordinal);
        if (reference is null) return result;
        foreach (var asset in reference.Assets)
        {
            ct.ThrowIfCancellationRequested(); DdsInfo? info = null; string? problem = null; long bytes = asset.Length;
            try { using var stream = asset.Open(); bytes = stream.CanSeek ? stream.Length : asset.Length; info = Dds.Inspect(stream, bytes).Info; }
            catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException) { problem = e.Message; }
            result.Add(asset.Path, new(asset.Path, bytes, info, problem));
            if (result.Count % 100 == 0 || result.Count == reference.Assets.Count)
                progress?.Report(new("Reference", result.Count, reference.Assets.Count, asset.Path, "Inspecting previous optimized textures"));
        }
        return result;
    }

    public static string Fingerprint(ReferenceCatalog reference, IReadOnlyDictionary<string, ReferenceTexture> textures) =>
        JsonFiles.Hash(JsonSerializer.Serialize(new { reference.Folder, reference.Placement, reference.Priority,
            Textures = textures.Values.OrderBy(i => i.Path, StringComparer.Ordinal) }, JsonFiles.Options));

    public static void VerifyUnchanged(Catalog catalog, OptimizationPlan plan, CancellationToken ct)
    {
        if (catalog.Reference is null && plan.Comparison is null) return;
        if (catalog.Reference is null || plan.Comparison is null) throw new IOException("Reference comparison is missing. Analyze again.");
        var current = catalog.Reference.Refresh?.Invoke(ct) ?? catalog.Reference;
        if (Fingerprint(current, Read(current, null, ct)) != plan.Comparison.Fingerprint)
            throw new IOException("Reference output changed after analysis. Analyze again before publishing a replacement.");
    }

    public static bool Fits(DdsInfo proposed, int drop, bool rebuild, DdsInfo reference)
    {
        var target = Target(proposed, drop, rebuild);
        return target.Width <= reference.Width && target.Height <= reference.Height && target.PayloadBytes <= reference.PayloadBytes
            && CoversReferenceMips(target, reference);
    }

    public static bool CoversReferenceMips(DdsInfo proposed, DdsInfo reference) =>
        Math.Max(1, proposed.Width >> (proposed.MipCount - 1)) <= Math.Max(1, reference.Width >> (reference.MipCount - 1))
        && Math.Max(1, proposed.Height >> (proposed.MipCount - 1)) <= Math.Max(1, reference.Height >> (reference.MipCount - 1));

    public static DdsInfo Target(DdsInfo input, int drop, bool rebuild)
    {
        int width = Math.Max(1, input.Width >> drop), height = Math.Max(1, input.Height >> drop);
        var target = input with { Width = width, Height = height,
            MipCount = rebuild ? 1 + (int)Math.Log2(Math.Max(width, height)) : input.MipCount - drop };
        return target with { PayloadBytes = Enumerable.Range(0, target.MipCount).Sum(target.MipBytes) };
    }

    public static ReferenceReport? Compare(Catalog catalog, IReadOnlyList<PlanItem> plan, IReadOnlyDictionary<string, ReferenceTexture> references)
    {
        if (catalog.Reference is not { } reference) return null;
        var assets = catalog.Assets.ToDictionary(a => a.Path, StringComparer.Ordinal);
        var items = plan.ToDictionary(i => i.Path, StringComparer.Ordinal); var differences = new List<ReferenceDifference>();
        foreach (var old in references.Values.OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            if (!items.TryGetValue(old.Path, out var item))
            { differences.Add(new(old.Path, "", "MissingSource", "No original winner found for a path supplied by the previous output; its removal needs review.", old.Info, null, old.Info?.PayloadBytes, null)); continue; }
            if (item.Decision == Decision.ReferenceCopy)
            { differences.Add(new(item.Path, item.Origin, "ReferenceRetained", item.Reason, old.Info, old.Info, old.Info?.PayloadBytes, old.Info?.PayloadBytes)); continue; }
            if (assets[item.Path].Priority > reference.Priority)
            { differences.Add(new(item.Path, item.Origin, "HigherPriority", "An unchanged higher-priority source wins over both outputs.", item.Input, item.Input, item.Input?.PayloadBytes, item.Input?.PayloadBytes)); continue; }
            DdsInfo? proposed = item.Input is null ? null : ReferenceComparison.Target(item.Input, item.DropMips, item.Decision == Decision.Rebuild);
            string status = old.Info is null ? "UnknownReference" : proposed is null ? "UnknownSource"
                : proposed.Width > old.Info.Width || proposed.Height > old.Info.Height || proposed.PayloadBytes > old.Info.PayloadBytes ? "Larger"
                : !CoversReferenceMips(proposed, old.Info) ? "IncompleteMips"
                : proposed.PayloadBytes < old.Info.PayloadBytes ? "Smaller" : "SamePayload";
            string reason = status == "IncompleteMips" ? "The retained chain stops before the reference's smallest mip levels. " + item.Reason : old.Problem ?? item.Reason;
            differences.Add(new(item.Path, item.Origin, status, reason, old.Info, proposed, old.Info?.PayloadBytes, proposed?.PayloadBytes));
        }
        foreach (var item in plan.Where(i => !references.ContainsKey(i.Path) && i.Decision is Decision.Reduce or Decision.Rebuild))
        {
            var proposed = Target(item.Input!, item.DropMips, item.Decision == Decision.Rebuild);
            differences.Add(new(item.Path, item.Origin, proposed.PayloadBytes > item.Input!.PayloadBytes ? "Larger" : "NewReduction",
                "Compared with the original winner; the reference had no override at this path.", item.Input, proposed, item.Input.PayloadBytes, proposed.PayloadBytes));
        }
        return new(reference.Folder, reference.Placement, Fingerprint(reference, references), differences);
    }
}

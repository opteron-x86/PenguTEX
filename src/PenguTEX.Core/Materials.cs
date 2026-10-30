namespace PenguTEX.Core;

public enum MaterialRole { Unknown, Color, Normal, ModelNormal, Height, Data, Cutout, Blend }
public sealed record MaterialUse(string Mesh, string Container, int Shape, int Shader, int Slot,
    MaterialRole Role, int AlphaCutoff, string Detail);
public sealed record MaterialHint(string Status, MaterialRole Role, int AlphaCutoff, int UseCount,
    IReadOnlyList<MaterialUse> Examples)
{
    public TextureSemantic? Semantic(DdsInfo info) => Status != "Resolved" ? null : Role switch
    {
        MaterialRole.Color => TextureSemantic.SrgbColor,
        MaterialRole.Cutout => TextureSemantic.OpacitySrgb,
        MaterialRole.Blend => TextureSemantic.OpacityBlendSrgb,
        MaterialRole.Normal => info.Format == "BC5" || (!info.IsBlockCompressed && info.RedMask != 0 && info.GreenMask != 0 && info.BlueMask == 0 && !info.Luminance)
            ? TextureSemantic.NormalRg : TextureSemantic.NormalRgb,
        MaterialRole.ModelNormal => info.Format is "BC4" or "BC5" ? null : TextureSemantic.NormalRgb,
        MaterialRole.Data or MaterialRole.Height => TextureSemantic.LinearData,
        _ => null,
    };
    public static MaterialHint Summarize(IReadOnlyList<MaterialUse> uses)
    {
        var choices = uses.Select(u => (u.Role, u.AlphaCutoff)).Distinct().ToArray();
        bool unknown = uses.Any(u => u.Role == MaterialRole.Unknown);
        return new(unknown ? "Unsupported binding" : choices.Length != 1 ? "Conflicting bindings" : "Resolved",
            !unknown && choices.Length == 1 ? choices[0].Role : MaterialRole.Unknown,
            choices.Length == 1 ? choices[0].AlphaCutoff : 128, uses.Count, uses.Take(4).ToArray());
    }
}
public sealed record MaterialCatalog(string Fingerprint, IReadOnlyDictionary<string, MaterialHint> Textures,
    int MeshCount, int UnreadableMeshes)
{
    public Func<CancellationToken, string>? RefreshFingerprint { get; init; }
    public void VerifyUnchanged(string? expected, CancellationToken ct)
    {
        if (expected != Fingerprint || RefreshFingerprint?.Invoke(ct) != expected)
            throw new IOException("Material sources changed or cannot be rechecked. Analyze again to review texture semantics.");
    }
}
public sealed record CoverageItem(string Path, string Status, string Original, string Proposed, string Reason);
public sealed record NativeCoverage(int TextureCount, int AboveLimit, int IncompleteMips, int Invalid,
    int Unsupported, int Protected, int UnreadableMaterialMeshes, bool SourcesComplete, IReadOnlyList<CoverageItem> Exceptions)
{
    public bool MeetsPolicy => SourcesComplete && UnreadableMaterialMeshes == 0 && AboveLimit == 0 && IncompleteMips == 0 && Invalid == 0 && Unsupported == 0;
    public string Assessment => $"Original-source coverage: {TextureCount:N0} textures; {AboveLimit:N0} above limits; {IncompleteMips:N0} incomplete mip chains; {Invalid:N0} invalid; {Unsupported:N0} unsupported; {Protected:N0} deliberately protected; {UnreadableMaterialMeshes:N0} unreadable material meshes. "
        + (MeetsPolicy ? "Selected limits and mip coverage are satisfied for assessed, unprotected textures. " : "Review unresolved originals and deliberate exclusions. ")
        + "Payload estimates are not resident VRAM; validate visuals and performance in game.";
    public static NativeCoverage Assess(OptimizationPlan plan)
    {
        int above = 0, missing = 0, invalid = 0, unsupported = 0, protect = 0;
        var rows = new List<CoverageItem>();
        foreach (var i in plan.Items)
        {
            string? status = null;
            if (i.Decision == Decision.Invalid) { invalid++; status = "Invalid original"; }
            else if (i.Decision == Decision.Unsupported) { unsupported++; status = "Unsupported"; }
            else if (i.Decision == Decision.Protected) { protect++; status = "Protected"; }
            else if (i.Input is { } info)
            {
                int cap = plan.Policy.Classify(i.Path).Maximum;
                if (Math.Max(i.TargetWidth, i.TargetHeight) > cap) { above++; status = "Above limit"; }
                int mips = i.Decision == Decision.Rebuild ? 1 + (int)Math.Log2(Math.Max(i.TargetWidth, i.TargetHeight)) : info.MipCount - i.DropMips;
                if (mips < 1 + (int)Math.Log2(Math.Max(i.TargetWidth, i.TargetHeight)))
                { missing++; status = status is null ? "Missing mip levels" : status + "; missing mips"; }
            }
            if (status is not null) rows.Add(new(i.Path, status, i.Input is { } input ? $"{input.Width}×{input.Height} / {input.MipCount} mips" : "Unreadable",
                i.TargetWidth > 0 ? $"{i.TargetWidth}×{i.TargetHeight}" : "Unchanged", i.Reason));
        }
        return new(plan.Items.Count, above, missing, invalid, unsupported, protect, plan.UnreadableMaterialMeshes, plan.CompleteSource, rows);
    }
}

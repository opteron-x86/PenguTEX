using PenguTEX.Assets;
using PenguTEX.Core;

namespace PenguTEX.Tests;

public sealed class ReferenceTests
{
    private static Policy Small => new() { Maximum = 8, NormalMaximum = 8, MaterialMaximum = 8 };
    private static Catalog Catalog(Fixture f) => CatalogLoader.Load(new(Folder: f.Dir("source")) { ReferenceFolder = f.Dir("reference") });

    [Fact] public void ComparisonCountsLargerOriginalsMissingSourcesAndUnknownLayouts()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds(16)); f.File("reference/a.dds", Fixture.Dds(4, 4));
        f.File("reference/removed.dds", Fixture.Dds());
        f.File("source/b.dds", Fixture.Dds()); f.File("reference/b.dds", [1, 2, 3]);
        f.File("source/c.dds", [1, 2, 3]); f.File("reference/c.dds", Fixture.Dds());
        var plan = Planner.Analyze(Catalog(f), Small); var report = Assert.IsType<ReferenceReport>(plan.Comparison);
        Assert.Equal(1, report.LargerCount); Assert.Equal(3, report.UnknownCount); Assert.True(report.HasUnresolved);
        Assert.Contains(report.Items, p => p.Status == "MissingSource");
        Assert.Contains(report.Items, p => p.Status == "UnknownReference"); Assert.Contains(report.Items, p => p.Status == "UnknownSource");
    }
    [Fact] public void ReferenceFittingPreservesFormatAndMeetsPayloadAsWellAsDimensions()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds(16, 16, dxgi: 98));
        f.File("reference/a.dds", Fixture.Dds(8, 8, dxgi: 71));
        var catalog = Catalog(f); var plan = Planner.Analyze(catalog, Small with { FitReference = true });
        var item = Assert.Single(plan.Items); Assert.Equal(4, item.TargetWidth); Assert.Equal("BC7", item.Input!.Format);
        Assert.False(plan.Comparison!.HasUnresolved); Assert.Contains("without changing format", item.Reason);
        string output = Path.Combine(f.Root, "output");
        var result = JobRunner.Build(catalog, plan, output, Path.Combine(f.Root, "cache"));
        Assert.False(result.Manifest.Comparison!.HasUnresolved);
        using var file = File.OpenRead(Path.Combine(output, "textures/a.dds")); Assert.Equal(98, Dds.Inspect(file).Info.Dxgi);
    }
    [Fact] public void LargerDimensionsAreReportedEvenWhenOneBlockHasTheSamePayload()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds(4, 4, mipCount: 1));
        f.File("reference/a.dds", Fixture.Dds(1, 1, mipCount: 1));
        var report = Planner.Analyze(Catalog(f), Small).Comparison!;
        Assert.Equal(1, report.LargerCount); Assert.Equal(0, report.KnownPayloadChange);
    }
    [Fact] public void MissingTailMipsAreAGapEvenWhenPayloadAndDimensionsAreSmaller()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds(8, 8, mipCount: 2));
        f.File("reference/a.dds", Fixture.Dds(16, 16)); var catalog = Catalog(f);
        var plain = Planner.Analyze(catalog, Small);
        Assert.Equal(0, plain.Comparison!.LargerCount); Assert.Equal(1, plain.Comparison.IncompleteMipCount); Assert.True(plain.Comparison.HasUnresolved);
        Assert.False(ReferenceComparison.Fits(plain.Items[0].Input!, 1, false, plain.Comparison.Items[0].Previous!));
        Assert.True(ReferenceComparison.Fits(plain.Items[0].Input!, 1, true, plain.Comparison.Items[0].Previous!));
        var fitted = Planner.Analyze(catalog, Small with { FitReference = true });
        Assert.True(fitted.Comparison!.HasUnresolved);
        Assert.DoesNotContain(fitted.Items, i => i.Decision == Decision.ReferenceCopy);
    }
    [Fact] public void SmallerCompleteChainsAndEquivalentPartialTailsAreNotFalseGaps()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds(8, 8)); f.File("reference/a.dds", Fixture.Dds(16, 16));
        f.File("source/b.dds", Fixture.Dds(8, 4, mipCount: 2)); f.File("reference/b.dds", Fixture.Dds(16, 8, mipCount: 3));
        var plan = Planner.Analyze(Catalog(f), Small); Assert.False(plan.Comparison!.HasUnresolved);
        Assert.Equal(0, plan.Comparison.IncompleteMipCount);
    }
    [Fact] public void ProtectedReferenceFitNeedsOptInAndKeepsHardOrExplicitProtections()
    {
        using var f = new Fixture();
        foreach (string path in new[] { "actors/character/body.dds", "landscape/ground.dds", "terrain/atlas.dds", "armor/custom.dds" })
        { f.File("source/" + path, Fixture.Dds()); f.File("reference/" + path, Fixture.Dds(4, 4)); }
        var catalog = Catalog(f); var policy = Small with { FitReference = true, Rules = [new("*/custom.dds", Protect: true)] };
        Assert.All(Planner.Analyze(catalog, policy).Items, i => Assert.Equal(Decision.Protected, i.Decision));
        var fitted = Planner.Analyze(catalog, policy with { FitProtectedReference = true });
        Assert.Equal(2, fitted.ChangedCount); Assert.Equal(2, fitted.Comparison!.LargerCount);
        Assert.Contains(fitted.Items, i => i.Path.Contains("terrain/") && i.Decision == Decision.Protected);
        Assert.Contains(fitted.Items, i => i.Path.EndsWith("custom.dds") && i.Decision == Decision.Protected);
    }
    [Fact] public void InsufficientMipsRemainAnExplicitComparisonGap()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds(mipCount: 1)); f.File("reference/a.dds", Fixture.Dds(4, 4));
        var plan = Planner.Analyze(Catalog(f), Small with { FitReference = true });
        Assert.Equal(Decision.Keep, plan.Items[0].Decision); Assert.Contains("No existing mip fits", plan.Items[0].Reason);
        Assert.Equal(1, plan.Comparison!.LargerCount);
    }
    [Fact] public void ReferenceIsProtectedFromOutputAndCacheWrites()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("reference/a.dds", Fixture.Dds(4, 4));
        var catalog = Catalog(f); var plan = Planner.Analyze(catalog, Small);
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, f.Dir("reference"), f.Dir("cache")));
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, f.Dir("output"), f.Dir("reference")));
        Assert.Throws<ArgumentException>(() => CatalogLoader.Load(new(Folder: f.Root) { ReferenceFolder = f.Dir("reference") }));
    }
    [Fact] public void ChangedReferenceMetadataChangesFingerprint()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("reference/a.dds", Fixture.Dds(4, 4));
        string before = Planner.Analyze(Catalog(f), Small).Comparison!.Fingerprint;
        f.File("reference/a.dds", Fixture.Dds(8, 8)); Assert.NotEqual(before, Planner.Analyze(Catalog(f), Small).Comparison!.Fingerprint);
    }
    private sealed class CallbackProgress(Action<JobProgress> callback) : IProgress<JobProgress>
    { public void Report(JobProgress progress) => callback(progress); }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReferenceChangesDuringGenerationPreventPublication(bool addPath)
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("reference/a.dds", Fixture.Dds(4, 4));
        var catalog = Catalog(f); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        JobRunner.Build(catalog, plan, output, cache); string before = File.ReadAllText(Path.Combine(output, JobRunner.ManifestName));
        var progress = new CallbackProgress(p => { if (p.Phase == "Build" && p.Done == 1) f.File("reference/" + (addPath ? "new.dds" : "a.dds"), Fixture.Dds(8, 8)); });
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output, cache, progress: progress));
        Assert.Equal(before, File.ReadAllText(Path.Combine(output, JobRunner.ManifestName)));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void ProfileReferenceUsesItsActualSlotAndNeverBecomesAnOriginal(bool enabled)
    {
        using var f = new Fixture(); string game = f.Dir("game"); f.Dir("game/Data");
        f.Text("instance/ModOrganizer.ini", $"[General]\ngamePath={game}\nselected_profile=Example\n");
        f.Text("instance/profiles/Example/settings.ini", "[General]\nLocalSettings=true\n");
        f.Text("instance/profiles/Example/skyrim.ini", "[Archive]\nsResourceArchiveList=\nsResourceArchiveList2=\n");
        f.Text("instance/profiles/Example/modlist.txt", "+High\n" + (enabled ? "+" : "-") + "Previous\n+Low\n");
        f.File("instance/mods/Previous/textures/a.dds", Fixture.Dds(4, 4));
        f.File("instance/mods/Previous/textures/b.dds", Fixture.Dds(4, 4));
        f.File("instance/mods/Low/textures/a.dds", Fixture.Dds(32, 32));
        f.File("instance/mods/High/textures/b.dds", Fixture.Dds(32, 32));
        var options = new SourceOptions(Instance: f.Dir("instance")) { ReferenceFolder = f.Dir("instance/mods/Previous") };
        var catalog = CatalogLoader.Load(options); Assert.DoesNotContain(catalog.Diagnostics, d => d.Severity == "Error");
        Assert.DoesNotContain(catalog.Assets, a => a.Origin == "Previous");
        var plan = Planner.Analyze(catalog, Small with { FitReference = true });
        Assert.Equal(1, plan.ChangedCount); Assert.False(plan.Comparison!.HasUnresolved);
        Assert.Contains(plan.Comparison.Items, i => i.Path.EndsWith("b.dds") && i.Status == "HigherPriority");
        Assert.Equal("Previous", plan.Comparison.Placement);
    }
    [Fact] public void PresetsDeclareSmallerLimitsAndLegacyNumericPoliciesArePreserved()
    {
        var quality = Policy.Preset("quality"); Assert.Equal(2048, quality.Maximum); Assert.Equal(1024, quality.NormalMaximum);
        Assert.Equal(1024, quality.Classify("textures/stone_p.dds").Maximum);
        Assert.Equal(256, Policy.Preset("compact").Classify("textures/stone_p.dds").Maximum);
        var old = System.Text.Json.JsonSerializer.Deserialize<Policy>("{\"Name\":\"Quality\",\"Maximum\":4096,\"NormalMaximum\":2048,\"MaterialMaximum\":2048}", JsonFiles.Options)!;
        Assert.Equal(4096, old.Maximum); Assert.Equal(2048, old.Classify("textures/stone_p.dds").Maximum);
        Assert.Throws<ArgumentException>(() => (quality with { FitProtectedReference = true }).Validate());
        using var f = new Fixture(); Assert.Throws<ArgumentException>(() => Planner.Analyze(CatalogLoader.Load(new(Folder: f.Root)), quality with { FitReference = true }));
    }
    [Theory] [InlineData("missing")] [InlineData("invalid")] [InlineData("missing-mips")]
    public void UnresolvedOriginalsNeverBecomeReferenceCopies(string kind)
    {
        using var f = new Fixture(); f.File("reference/a.dds", Fixture.Dds(4, 4)); f.Dir("source");
        if (kind != "missing") f.File("source/a.dds", kind == "invalid" ? [1, 2, 3] : Fixture.Dds(mipCount: 1));
        var catalog = Catalog(f); var policy = Small with { FitReference = true };
        var plan = Planner.Analyze(catalog, policy);
        Assert.True(plan.Comparison!.HasUnresolved); Assert.Equal(0, plan.Comparison.CarriedCount);
        Assert.DoesNotContain(plan.Items, i => i.Decision == Decision.ReferenceCopy);
        Assert.Contains("no longer supported", Assert.Throws<ArgumentException>(() => Planner.Analyze(catalog, policy with { CarryReference = true })).Message);
    }
    [Fact] public void StaleReferenceCopyPlansAreRejectedBeforeAnyWrites()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("reference/a.dds", Fixture.Dds(4, 4));
        var catalog = Catalog(f); var plan = Planner.Analyze(catalog, Small);
        var stale = plan with { Items = [plan.Items[0] with { Decision = Decision.ReferenceCopy, Container = catalog.Reference!.Assets[0].Container }] };
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        Assert.Throws<InvalidDataException>(() => JobRunner.Build(catalog, stale, output, cache));
        Assert.False(Directory.Exists(output)); Assert.False(Directory.Exists(cache));
        Assert.Throws<ArgumentException>(() => JobRunner.Build(catalog, plan with { Policy = Small with { CarryReference = true } }, output, cache));
        Assert.False(Directory.Exists(cache));
    }
    [Fact] public void ReferenceAssetsCannotBeDisguisedAsOriginalCatalogWinners()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("reference/a.dds", Fixture.Dds(4, 4));
        var catalog = Catalog(f); var plan = Planner.Analyze(catalog, Small);
        string cache = Path.Combine(f.Root, "cache");
        Assert.Throws<InvalidDataException>(() => JobRunner.Build(catalog with { Assets = catalog.Reference!.Assets }, plan, Path.Combine(f.Root, "output"), cache));
        Assert.False(Directory.Exists(cache));
    }
    [Fact] public void CachedReferenceOperationIsRegeneratedFromTheOriginal()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("reference/a.dds", Fixture.Dds(4, 4));
        var catalog = Catalog(f); var plan = Planner.Analyze(catalog, Small);
        string output = f.Dir("output"), cache = f.Dir("cache");
        var first = JobRunner.Build(catalog, plan, output, cache);
        var entry = Assert.Single(first.Manifest.Files); Assert.Equal(catalog.Assets[0].Container, entry.SourceContainer);
        JsonFiles.Write(Path.Combine(cache, entry.Recipe + ".dds.json"), entry with { Operation = "ReferenceCopy" });
        var rebuilt = JobRunner.Build(catalog, plan, output, cache);
        Assert.Equal(0, rebuilt.Reused); Assert.True(rebuilt.Manifest.OriginalsOnly);
        Assert.Equal(catalog.Assets[0].Container, rebuilt.Manifest.Files[0].SourceContainer);
        var cached = JobRunner.Build(catalog, plan, output, cache);
        Assert.Equal(1, cached.Reused); Assert.Equal(catalog.Assets[0].Container, cached.Manifest.Files[0].SourceContainer);
        var legacy = first.Manifest with { Files = [entry with { Operation = "ReferenceCopy" }] };
        JsonFiles.Write(Path.Combine(output, JobRunner.ManifestName), legacy);
        Assert.False(JobRunner.Verify(output).OriginalsOnly);
        var report = plan.Comparison! with { Items = [plan.Comparison.Items[0] with { Status = "ReferenceRetained" }] };
        Assert.True(report.HasUnresolved); Assert.Contains("originals-only", report.Assessment);
        Assert.True(JobRunner.Build(catalog, plan, output, cache).Manifest.OriginalsOnly);
    }
}

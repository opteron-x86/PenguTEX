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
    public void CarryForwardIsOptInAndCopiesOnlyValidatedReferenceFilesExactly(string kind)
    {
        using var f = new Fixture(); var previous = Fixture.Dds(4, 4); f.File("reference/a.dds", previous);
        f.Dir("source");
        if (kind != "missing") f.File("source/a.dds", kind == "invalid" ? [1, 2, 3] : Fixture.Dds(mipCount: 1));
        var catalog = Catalog(f); var policy = Small with { FitReference = true };
        Assert.True(Planner.Analyze(catalog, policy).Comparison!.HasUnresolved);
        var plan = Planner.Analyze(catalog, policy with { CarryReference = true });
        Assert.Equal(1, plan.Comparison!.CarriedCount); Assert.False(plan.Comparison.HasUnresolved);
        Assert.Equal(Decision.ReferenceCopy, Assert.Single(plan.Items).Decision);
        string output = f.Dir("output"), cache = f.Dir("cache"); var result = JobRunner.Build(catalog, plan, output, cache);
        Assert.Equal(previous, System.IO.File.ReadAllBytes(Path.Combine(output, "textures/a.dds")));
        Assert.Equal(previous, System.IO.File.ReadAllBytes(Path.Combine(f.Root, "reference/a.dds")));
        Assert.Equal("ReferenceCopy", Assert.Single(result.Manifest.Files).Operation);
        Assert.Equal(result.Manifest.Files[0].SourceHash, result.Manifest.Files[0].OutputHash);
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Reused);
    }
    [Fact] public void CarryForwardKeepsProtectionAndUnreadableReferenceGapsVisible()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("reference/a.dds", [1, 2, 3]);
        f.File("reference/terrain/lod.dds", Fixture.Dds()); f.File("reference/protected.dds", Fixture.Dds());
        var plan = Planner.Analyze(Catalog(f), Small with { CarryReference = true, Rules = [new("*/protected.dds", Protect: true)] });
        Assert.Equal(0, plan.Comparison!.CarriedCount); Assert.Equal(3, plan.Comparison.UnknownCount);
    }
    [Fact] public void ChangingReferencePixelsDuringCopyNeverPublishesOrPoisonsCache()
    {
        using var f = new Fixture(); var original = Fixture.Dds(4, 4); var changed = (byte[])original.Clone(); changed[^1] ^= 33;
        f.File("reference/a.dds", original); var catalog = Catalog(f);
        var plan = Planner.Analyze(catalog, Small with { CarryReference = true }); int opens = 0;
        catalog = catalog with { Reference = catalog.Reference! with { Refresh = null,
            Assets = [catalog.Reference.Assets[0] with { Open = () => new MemoryStream(++opens <= 2 ? original : changed) }] } };
        string output = Path.Combine(f.Root, "output"), cache = f.Dir("cache");
        Assert.ThrowsAny<Exception>(() => JobRunner.Build(catalog, plan, output, cache));
        Assert.False(Directory.Exists(output)); Assert.Empty(Directory.GetFiles(cache, "*.dds"));
    }
}

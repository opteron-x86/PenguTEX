using PenguTEX.Assets;
using PenguTEX.Core;

namespace PenguTEX.Tests;

public class WorkflowTests
{
    private static Policy Small => new() { Maximum = 8, NormalMaximum = 8, MaterialMaximum = 8 };
    [Fact] public void BuildVerifyResumeAndDetectChangedContent()
    {
        using var f = new Fixture(); var source = f.Dir("source"); string input = f.File("source/textures/armor/test.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: source)); var plan = Planner.Analyze(catalog, Small);
        Assert.Single(plan.Items); Assert.Equal(1, plan.ChangedCount);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var result = JobRunner.Build(catalog, plan, output, cache); Assert.Equal(1, result.Files); Assert.Equal(0, result.Reused);
        Assert.Single(JobRunner.Verify(output).Files);
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Reused);
        var time = File.GetLastWriteTimeUtc(input); byte[] changed = File.ReadAllBytes(input); changed[^1] ^= 31;
        File.WriteAllBytes(input, changed); File.SetLastWriteTimeUtc(input, time);
        var rebuilt = JobRunner.Build(catalog, plan, output, cache); Assert.Equal(0, rebuilt.Reused);
        Assert.NotEqual(result.Manifest.Files[0].OutputHash, rebuilt.Manifest.Files[0].OutputHash);
    }
    [Fact] public void CorruptCacheIsRebuiltAndCorruptPublishedOutputIsDetected()
    {
        using var f = new Fixture(); var source = f.Dir("source"); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: source)); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var first = JobRunner.Build(catalog, plan, output, cache);
        File.WriteAllBytes(Directory.GetFiles(cache, "*.dds")[0], new byte[9]);
        Assert.Equal(0, JobRunner.Build(catalog, plan, output, cache).Reused);
        File.WriteAllBytes(Path.Combine(output, "textures/a.dds"), new byte[2]);
        Assert.ThrowsAny<IOException>(() => JobRunner.Verify(output));
    }
    [Fact] public void FailedBuildPreservesPublishedOutput()
    {
        using var f = new Fixture(); var source = f.Dir("source"); var path = f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: source)); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        JobRunner.Build(catalog, plan, output, cache); byte[] original = File.ReadAllBytes(Path.Combine(output, "textures/a.dds"));
        File.WriteAllBytes(path, new byte[2]);
        Assert.ThrowsAny<Exception>(() => JobRunner.Build(catalog, plan, output, cache));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(output, "textures/a.dds"))); JobRunner.Verify(output);
    }
    [Fact] public void CancellationPreservesOutputAndRerunWorks()
    {
        using var f = new Fixture(); var source = f.Dir("source"); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: source)); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        JobRunner.Build(catalog, plan, output, cache); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => JobRunner.Build(catalog, plan, output, cache, ct: cancel.Token));
        JobRunner.Verify(output); Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Reused);
    }
    [Fact] public void RejectsForeignOutputAndSourceOverlap()
    {
        using var f = new Fixture(); var source = f.Dir("source"); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: source)); var plan = Planner.Analyze(catalog, Small);
        string cache = Path.Combine(f.Root, "cache");
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, Path.Combine(source, "output"), cache));
        var foreign = f.Dir("foreign"); f.Text("foreign/keep.txt", "keep");
        Assert.ThrowsAny<IOException>(() => JobRunner.Build(catalog, plan, foreign, cache)); Assert.True(File.Exists(Path.Combine(foreign, "keep.txt")));
    }
    [Fact] public void ExtraUserFilePreventsReplacement()
    {
        using var f = new Fixture(); var source = f.Dir("source"); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: source)); var plan = Planner.Analyze(catalog, Small);
        var output = Path.Combine(f.Root, "output"); var cache = Path.Combine(f.Root, "cache");
        JobRunner.Build(catalog, plan, output, cache); f.Text("output/personal.txt", "keep");
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output, cache)); Assert.True(File.Exists(Path.Combine(output, "personal.txt")));
    }
    [Fact] public void ChangingPolicyRemovesObsoleteGeneratedFiles()
    {
        using var f = new Fixture(); var source = f.Dir("source"); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: source)); var output = Path.Combine(f.Root, "output"); var cache = Path.Combine(f.Root, "cache");
        JobRunner.Build(catalog, Planner.Analyze(catalog, Small), output, cache);
        JobRunner.Build(catalog, Planner.Analyze(catalog, Small with { Rules = [new("*", Protect: true)] }), output, cache);
        Assert.False(File.Exists(Path.Combine(output, "textures/a.dds"))); Assert.Empty(JobRunner.Verify(output).Files);
    }
    [Theory] [InlineData("textures/../escape.dds")] [InlineData("/absolute.dds")] [InlineData("textures/C:/bad.dds")]
    public void RejectsUnsafeOutputPaths(string path)
    { using var f = new Fixture(); Assert.Throws<InvalidDataException>(() => JobRunner.Resolve(f.Root, path)); }
    [Fact] public void InterruptedDirectorySwapRestoresThenRebuildsOutput()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var first = JobRunner.Build(catalog, plan, output, cache);
        string backup = Path.Combine(f.Root, ".pengutex-previous-" + JsonFiles.Hash(output)[..20]); Directory.Move(output, backup);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => JobRunner.Build(catalog, plan, output, cache, ct: cancel.Token));
        Assert.Equal(first.Manifest.Files[0].OutputHash, JobRunner.Verify(output).Files[0].OutputHash);
        Assert.False(Directory.Exists(backup)); Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Reused);
    }
    [Fact] public void OutputLockAndSymbolicLinksPreventConflictingWrites()
    {
        if (OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        string lockPath = Path.Combine(f.Root, ".pengutex-" + JsonFiles.Hash(output)[..20] + ".lock");
        using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output, cache));
        JobRunner.Build(catalog, plan, output, cache);
        var external = f.Text("outside/keep.txt", "preserve"); File.CreateSymbolicLink(Path.Combine(output, "link.txt"), external);
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output, cache)); Assert.Equal("preserve", File.ReadAllText(external));
        string linkedOutput = Path.Combine(f.Root, "linked-output"); Directory.CreateSymbolicLink(linkedOutput, output);
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, linkedOutput, cache));
    }
    [Fact] public void ProtectionsAndExplicitOverridesAreExplained()
    {
        var policy = Small; Assert.NotNull(policy.Classify("textures/landscape/rocks/a_n.dds").Protection);
        var custom = policy with { Rules = [new("textures/landscape/*", Maximum: 8)] };
        Assert.Null(custom.Classify("textures/landscape/rocks/a_n.dds").Protection);
        Assert.Equal("Normal", custom.Classify("textures/landscape/rocks/a_n.dds").Category);
        Assert.Throws<ArgumentException>(() => (policy with { Maximum = 31 }).Validate());
    }
    [Fact] public void MissingMipsAreKeptWithReason()
    {
        using var f = new Fixture(); f.File("input/a.dds", Fixture.Dds(mipCount: 1));
        var plan = Planner.Analyze(CatalogLoader.Load(new(Folder: Path.Combine(f.Root, "input"))), Small);
        Assert.Equal(Decision.Keep, plan.Items[0].Decision); Assert.Contains("No existing mip", plan.Items[0].Reason);
    }
    [Fact] public void MalformedTextureIsReportedWithoutAbortingInventory()
    {
        using var f = new Fixture(); var data = Fixture.Dds(); data[4] = 0; f.File("input/bad.dds", data);
        f.File("input/good.dds", Fixture.Dds());
        var plan = Planner.Analyze(CatalogLoader.Load(new(Folder: Path.Combine(f.Root, "input"))), Small);
        Assert.Contains(plan.Items, x => x.Decision == Decision.Invalid); Assert.Equal(1, plan.ChangedCount);
    }
}

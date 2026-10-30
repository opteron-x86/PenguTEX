using PenguTEX.Assets;
using PenguTEX.Assets.Paths;
using PenguTEX.Core;

namespace PenguTEX.Tests;

public class WorkflowTests
{
    private static Policy Small => new() { Maximum = 8, NormalMaximum = 8, MaterialMaximum = 8 };
    [Fact] public void RawAndHdrTexturesBuildAndReuseVerifiedCache()
    {
        using var f = new Fixture(); f.File("source/raw.dds", Fixture.RawDds(32, 8, bits: 24, alignment: 4));
        f.File("source/hdr.dds", Fixture.Dds(32, 8, dxgi: 95));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        Assert.Equal(2, plan.ChangedCount);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        Assert.Equal(2, JobRunner.Build(catalog, plan, output, cache).Files);
        Assert.Equal(2, JobRunner.Verify(output).Files.Count);
        Assert.Equal(2, JobRunner.Build(catalog, plan, output, cache).Reused);
        var rebuild = Planner.Analyze(catalog, Small with { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        Assert.Equal(Decision.Unsupported, rebuild.Items.Single(i => i.Input!.Format == "BC6H").Decision);
        Assert.Equal(Decision.Rebuild, rebuild.Items.Single(i => !i.Input!.IsBlockCompressed).Decision);
    }
    [Fact] public void FirstBuildAcceptsAnExistingEmptyDirectory()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = f.Dir("chosen output"), cache = Path.Combine(f.Root, "cache");
        var result = JobRunner.Build(catalog, plan, output + Path.DirectorySeparatorChar, cache);
        Assert.Equal(1, result.Files); Assert.Single(JobRunner.Verify(output).Files);
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Reused);
    }
    private sealed class CallbackProgress(Action<JobProgress> callback) : IProgress<JobProgress>
    { public void Report(JobProgress value) => callback(value); }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(24)]
    public void ProgressReachesEveryPhaseBeforeOutputIsReady(int count)
    {
        using var f = new Fixture();
        for (int i = 0; i < count; i++) f.File($"source/texture{i}.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var events = new List<JobProgress>();
        var progress = new CallbackProgress(p =>
        {
            events.Add(p);
            if (p.Phase == "Verify") Assert.False(Directory.Exists(output));
            if (p.Phase == "Publish" && p.Done == 1) Assert.Equal(count, JobRunner.Verify(output).Files.Count);
        });
        JobRunner.Build(catalog, plan, output, cache, workers: 8, progress: progress);
        Assert.Equal(Enumerable.Range(0, count + 1), events.Where(p => p.Phase == "Build").Select(p => p.Done));
        var verification = events.Where(p => p.Phase == "Verify").ToArray();
        Assert.Equal(count, verification[^1].Done); Assert.All(verification, p => Assert.Equal(count, p.Total));
        Assert.Equal(count, verification.Where(p => p.Path.Length > 0).Select(p => p.Path).Distinct().Count());
        Assert.Equal(new[] { "Build", "Verify", "Publish" }, events.Select(p => p.Phase).Distinct());
        Assert.Equal(1, events[^1].Done);
        events.Clear();
        Assert.Equal(count, JobRunner.Build(catalog, plan, output, cache, workers: 8, progress: new CallbackProgress(events.Add)).Reused);
        Assert.Equal(new[] { "Build", "Verify", "Publish", "Cleanup" }, events.Select(p => p.Phase).Distinct());
        Assert.Equal(1, events[^1].Done);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void InterruptedVerificationDoesNotPublishOrReportSuccess(bool cancelVerification)
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var previous = JobRunner.Build(catalog, plan, output, cache);
        string stage = Path.Combine(f.Root, ".pengutex-stage-" + JsonFiles.Hash(HostPath.ComparisonKey(output))[..20]);
        using var cancel = new CancellationTokenSource(); var events = new List<JobProgress>();
        var progress = new CallbackProgress(p =>
        {
            events.Add(p);
            if (p.Phase != "Verify" || p.Done != 0 || p.Path.Length == 0) return;
            if (cancelVerification) cancel.Cancel();
            else
            {
                string file = Path.Combine(stage, p.Path); byte[] corrupt = File.ReadAllBytes(file);
                corrupt[^1] ^= 31; File.WriteAllBytes(file, corrupt);
            }
        });
        var error = Record.Exception(() => JobRunner.Build(catalog, plan, output, cache, progress: progress, ct: cancel.Token));
        if (cancelVerification) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.DoesNotContain(events, p => p.Phase is "Publish" or "Cleanup" || (p.Phase == "Verify" && p.Done == 1));
        Assert.Equal(previous.Manifest.Files[0].OutputHash, JobRunner.Verify(output).Files[0].OutputHash);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PublicationPreservesFilesAddedDuringGeneration(bool previousBuild)
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = f.Dir("output"), cache = Path.Combine(f.Root, "cache");
        if (previousBuild) JobRunner.Build(catalog, plan, output, cache);
        var progress = new CallbackProgress(p => { if (p.Phase == "Build") f.Text("output/personal.txt", "preserve"); });
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output, cache, progress: progress));
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(output, "personal.txt")));
        Assert.Equal(previousBuild, File.Exists(Path.Combine(output, JobRunner.ManifestName)));
    }
    [Fact] public void EmptyPreviousDirectoryRecoversAfterInterruptedFirstPublication()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        string backup = f.Dir(".pengutex-previous-" + JsonFiles.Hash(HostPath.ComparisonKey(output))[..20]);
        Assert.ThrowsAny<OperationCanceledException>(() => JobRunner.Build(catalog, plan, output, cache, ct: new CancellationToken(true)));
        Assert.True(Directory.Exists(output)); Assert.Empty(Directory.EnumerateFileSystemEntries(output)); Assert.False(Directory.Exists(backup));
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Files);
    }
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
        string backup = Path.Combine(f.Root, ".pengutex-previous-" + JsonFiles.Hash(HostPath.ComparisonKey(output))[..20]); Directory.Move(output, backup);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => JobRunner.Build(catalog, plan, output, cache, ct: cancel.Token));
        Assert.Equal(first.Manifest.Files[0].OutputHash, JobRunner.Verify(output).Files[0].OutputHash);
        Assert.False(Directory.Exists(backup)); Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Reused);
    }
    [Fact] public void OutputLockPreventsConflictingWritesIncludingWindowsCaseAliases()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var built = JobRunner.Build(catalog, plan, output, cache);
        string lockPath = Assert.Single(Directory.EnumerateFiles(f.Root, ".pengutex-*.lock"));
        using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output, cache));
            if (OperatingSystem.IsWindows())
                Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output.ToUpperInvariant(), cache));
        }
        Assert.Equal(built.Manifest.Files[0].OutputHash, JobRunner.Verify(output).Files[0].OutputHash);
    }
    [Fact] public void WindowsOpenOutputBlocksReplacementAndPreservesThePublishedMod()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var built = JobRunner.Build(catalog, plan, output, cache);
        using (var reader = File.OpenRead(Path.Combine(output, "textures/a.dds")))
        {
            Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output, cache));
            Assert.Equal(built.Manifest.Files[0].OutputHash, JobRunner.Verify(output).Files[0].OutputHash);
        }
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Reused);
        Assert.Equal(built.Manifest.Files[0].OutputHash, JobRunner.Verify(output).Files[0].OutputHash);
    }
    [Fact] public void SymbolicLinksPreventWritesOutsideOwnedOutput()
    {
        if (OperatingSystem.IsWindows()) return; // Creating symlinks requires an additional Windows privilege.
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        JobRunner.Build(catalog, plan, output, cache);
        var external = f.Text("outside/keep.txt", "preserve"); File.CreateSymbolicLink(Path.Combine(output, "link.txt"), external);
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, output, cache)); Assert.Equal("preserve", File.ReadAllText(external));
        string linkedOutput = Path.Combine(f.Root, "linked-output"); Directory.CreateSymbolicLink(linkedOutput, output);
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, linkedOutput, cache));
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, linkedOutput + Path.DirectorySeparatorChar, cache));
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

using PenguTEX.Assets;
using PenguTEX.Assets.Paths;
using PenguTEX.Core;

namespace PenguTEX.Tests;

public class WorkflowTests
{
    private static Policy Small => new() { Maximum = 8, NormalMaximum = 8, MaterialMaximum = 8 };
    [Fact] public void RawAndHdrTexturesBuildWithoutCachingMipCopies()
    {
        using var f = new Fixture(); f.File("source/raw.dds", Fixture.RawDds(32, 8, bits: 24, alignment: 4));
        f.File("source/hdr.dds", Fixture.Dds(32, 8, dxgi: 95));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        Assert.Equal(2, plan.ChangedCount);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        Assert.Equal(2, JobRunner.Build(catalog, plan, output, cache).Files);
        Assert.Equal(2, JobRunner.Verify(output).Files.Count);
        var first = JobRunner.Verify(output).Files.Select(e => e.OutputHash).ToArray();
        var again = JobRunner.Build(catalog, plan, output, cache); Assert.Equal(2, again.Files); Assert.Equal(0, again.Reused);
        Assert.Equal(first, again.Manifest.Files.Select(e => e.OutputHash)); Assert.Empty(Directory.GetFiles(CacheStore.Folder(cache)));
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
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Files);
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
        Assert.Equal(count, JobRunner.Build(catalog, plan, output, cache, workers: 8, progress: new CallbackProgress(events.Add)).Files);
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
        Assert.Equal(result.Manifest.Files[0].OutputHash, JobRunner.Build(catalog, plan, output, cache).Manifest.Files[0].OutputHash);
        var time = File.GetLastWriteTimeUtc(input); byte[] changed = File.ReadAllBytes(input); changed[^1] ^= 31;
        File.WriteAllBytes(input, changed); File.SetLastWriteTimeUtc(input, time);
        var rebuilt = JobRunner.Build(catalog, plan, output, cache); Assert.Equal(0, rebuilt.Reused);
        Assert.NotEqual(result.Manifest.Files[0].OutputHash, rebuilt.Manifest.Files[0].OutputHash);
    }
    [Fact] public void CorruptCacheIsRebuiltAndCorruptPublishedOutputIsDetected()
    {
        using var f = new Fixture(); var source = f.Dir("source"); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: source)); var plan = Planner.Analyze(catalog, Small with { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache"); var cpu = new PenguTEX.Imaging.CpuMipProcessor();
        var first = JobRunner.Build(catalog, plan, output, cache, processor: cpu);
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache, processor: cpu).Reused);
        File.WriteAllBytes(Directory.GetFiles(CacheStore.Folder(cache), "*.dds")[0], new byte[9]);
        var repaired = JobRunner.Build(catalog, plan, output, cache, processor: cpu); Assert.Equal(0, repaired.Reused);
        Assert.Equal(first.Manifest.Files[0].OutputHash, repaired.Manifest.Files[0].OutputHash);
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache, processor: cpu).Reused);
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
        JobRunner.Verify(output); Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Files);
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
        Assert.False(Directory.Exists(backup)); Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Files);
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
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache).Files);
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
    [Fact] public void PlainCapRulesKeepGeneratedProtectionUntilExplicitlyOverridden()
    {
        var capped = Small with { Rules = [new("textures/*", Maximum: 8)] };
        foreach (string path in new[] { "textures/interface/logo.dds", "textures/terrain/world/a.dds", "textures/lod/tree.dds", "textures/dyndolod/lod/a.dds", "textures/shaders/x.dds" })
        {
            var generated = capped.Classify(path); Assert.Equal(ProtectionKind.Generated, generated.Kind);
            Assert.Contains("'textures/*'", generated.Protection); Assert.Contains("OverrideGeneratedProtection", generated.Protection);
        }
        Assert.Equal(ProtectionKind.Characters, Small.Classify("textures/actors/character/face.dds").Kind);
        Assert.Equal(ProtectionKind.None, capped.Classify("textures/actors/character/face.dds").Kind);
        Assert.Equal(Policy.GeneratedProtection, Small.Classify("textures/interface/logo.dds").Protection);
        // An explicit override stays in force for a later, broader cap; other generated paths stay protected.
        var lifted = Small with { Rules = [new("textures/interface/*", OverrideGeneratedProtection: true), new("textures/*", Maximum: 16)] };
        var logo = lifted.Classify("textures/interface/logo.dds"); Assert.Null(logo.Protection); Assert.Equal(16, logo.Maximum);
        Assert.Equal(ProtectionKind.Generated, lifted.Classify("textures/lod/tree.dds").Kind);
        var again = lifted with { Rules = [..lifted.Rules, new("*/logo.dds", Protect: true)] };
        Assert.Equal(ProtectionKind.Rule, again.Classify("textures/interface/logo.dds").Kind);
        // A cap after an explicit Protect rule lifts that rule, but not the generated protection beneath it.
        var layered = Small with { Rules = [new("*/logo.dds", Protect: true), new("textures/*", Maximum: 8)] };
        Assert.Equal(ProtectionKind.Generated, layered.Classify("textures/interface/logo.dds").Kind);
        Assert.Equal(ProtectionKind.None, layered.Classify("textures/armor/logo.dds").Kind);
        Assert.Throws<ArgumentException>(() => (Small with { Rules = [new("*", Protect: true, OverrideGeneratedProtection: true)] }).Validate());
        // Existing policies keep their JSON text, and therefore their policy hash.
        Assert.DoesNotContain("OverrideGeneratedProtection", System.Text.Json.JsonSerializer.Serialize(capped, JsonFiles.Options));
        var parsed = System.Text.Json.JsonSerializer.Deserialize<PathRule>("{\"Pattern\":\"a\",\"OverrideGeneratedProtection\":true}", JsonFiles.Options)!;
        Assert.True(parsed.OverrideGeneratedProtection); Assert.Contains("OverrideGeneratedProtection", System.Text.Json.JsonSerializer.Serialize(parsed, JsonFiles.Options));
    }
    [Fact] public void BroadCapRuleReducesOrdinaryTexturesAndReportsProtectedGeneratedOnes()
    {
        using var f = new Fixture(); f.File("source/textures/interface/logo.dds", Fixture.Dds()); f.File("source/textures/armor/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, Small with { Maximum = 16, Rules = [new("textures/*", Maximum: 8)] });
        var logo = plan.Items.Single(i => i.Path.EndsWith("logo.dds")); Assert.Equal(Decision.Protected, logo.Decision);
        Assert.Contains("OverrideGeneratedProtection", logo.Reason);
        Assert.Equal(Decision.Reduce, plan.Items.Single(i => i.Path.EndsWith("a.dds")).Decision);
        plan = Planner.Analyze(catalog, Small with { Maximum = 16, Rules = [new("textures/*", Maximum: 8, OverrideGeneratedProtection: true)] });
        Assert.All(plan.Items, i => Assert.Equal(Decision.Reduce, i.Decision));
    }
    [Fact] public void BrieflyDeniedFileReplacementIsRetriedOnlyWhereItIsTransient()
    {
        int calls = 0; var waits = new List<int>();
        void Denied(int failures, Exception error) { if (++calls <= failures) throw error; }
        TransientIo.Retry(() => Denied(2, new UnauthorizedAccessException("Access to the path is denied.")), true, wait: waits.Add);
        Assert.Equal(3, calls); Assert.Equal([20, 40], waits);
        calls = 0; TransientIo.Retry(() => Denied(1, new IOException("The process cannot access the file.")), true, wait: waits.Add); Assert.Equal(2, calls);
        // Persistent denial reports the original error after the bounded attempts, waiting about two seconds in total.
        calls = 0; waits.Clear();
        Assert.Throws<UnauthorizedAccessException>(() => TransientIo.Retry(() => Denied(int.MaxValue, new UnauthorizedAccessException()), true, wait: waits.Add));
        Assert.Equal(10, calls); Assert.Equal(9, waits.Count); Assert.InRange(waits.Sum(), 1500, 2500);
        // Other platforms, missing files and unrelated failures are not retried.
        calls = 0; Assert.Throws<UnauthorizedAccessException>(() => TransientIo.Retry(() => Denied(1, new UnauthorizedAccessException()), false, wait: waits.Add)); Assert.Equal(1, calls);
        calls = 0; Assert.Throws<FileNotFoundException>(() => TransientIo.Retry(() => Denied(1, new FileNotFoundException()), true, wait: waits.Add)); Assert.Equal(1, calls);
        calls = 0; Assert.Throws<InvalidDataException>(() => TransientIo.Retry(() => Denied(1, new InvalidDataException()), true, wait: waits.Add)); Assert.Equal(1, calls);
        using var f = new Fixture(); string target = f.File("settings.json", [1]), temporary = f.File("settings.tmp", [2]);
        TransientIo.Replace(temporary, target); Assert.Equal([2], File.ReadAllBytes(target)); Assert.False(File.Exists(temporary));
    }
    [Fact] public void HashingStreamsCoverEveryByteIncludingWhatTheConsumerSkipped()
    {
        byte[] data = new byte[300_000]; new Random(5).NextBytes(data); string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        using (var partial = new HashingReadStream(new MemoryStream(data)))
        {
            byte[] head = new byte[1000]; partial.ReadExactly(head); Assert.Equal(data[..1000], head); Assert.Equal(data[1000], partial.ReadByte());
            Assert.Equal(expected, partial.Complete()); Assert.Equal(data.Length, partial.BytesRead); Assert.False(partial.CanSeek);
        }
        using (var cancelled = new HashingReadStream(new MemoryStream(data)))
            Assert.Throws<OperationCanceledException>(() => cancelled.Complete(new CancellationToken(true)));
        using var file = new MemoryStream(); using var output = new HashingWriteStream(file);
        output.Write(data, 0, 10); output.Write(data.AsSpan(10)); Assert.Equal(expected, output.Complete());
        Assert.Equal(data.Length, output.BytesWritten); Assert.Equal(data, file.ToArray());
    }
    [Fact] public void BuildReadsRebuildSourcesTwiceOrOnceWhenReusedAndMipCopySourcesOnce()
    {
        using var f = new Fixture(); byte[] original = Fixture.Dds(); f.File("source/a.dds", original);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); int opens = 0;
        var counted = catalog with { Assets = [catalog.Assets[0] with { Open = () => { Interlocked.Increment(ref opens); return new MemoryStream(original); } }] };
        // A rebuild from the top level never reads the smaller source mips, but the recorded hash still covers them.
        var plan = Planner.Analyze(counted, Small with { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        opens = 0; string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var built = JobRunner.Build(counted, plan, output, cache, processor: new PenguTEX.Imaging.CpuMipProcessor());
        Assert.Equal(2, opens); var entry = Assert.Single(built.Manifest.Files); Assert.Equal(TextureOperation.Rebuild, entry.Operation);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)), entry.SourceHash);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "textures/a.dds")))), entry.OutputHash);
        opens = 0; Assert.Equal(1, JobRunner.Build(counted, plan, output, cache, processor: new PenguTEX.Imaging.CpuMipProcessor()).Reused); Assert.Equal(1, opens);
        // A mip copy is not cached: one read produces both the output and the source hash.
        var copy = Planner.Analyze(counted, Small); opens = 0; string copies = Path.Combine(f.Root, "copies");
        var copied = JobRunner.Build(counted, copy, copies, cache); Assert.Equal(1, opens); Assert.Equal(TextureOperation.MipCopy, copied.Manifest.Files[0].Operation);
        Assert.Equal(entry.SourceHash, copied.Manifest.Files[0].SourceHash);
        opens = 0; Assert.Equal(0, JobRunner.Build(counted, copy, copies, cache).Reused); Assert.Equal(1, opens);
    }
    [Fact] public void RebuildsAreReusedAcrossPolicyChangesThatDoNotAffectTheirBytes()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("source/b.dds", Fixture.Dds(32, 32));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var cpu = new PenguTEX.Imaging.CpuMipProcessor();
        var policy = Small with { Name = "First", Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.LinearData)] };
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var first = JobRunner.Build(catalog, Planner.Analyze(catalog, policy), output, cache, processor: cpu); Assert.Equal(0, first.Reused);
        // A new name, an unrelated cap and an extra rule that matches nothing leave both textures' recipes unchanged.
        var renamed = policy with { Name = "Second", NormalMaximum = 4, Rules = [new("textures/unrelated/*", Protect: true), ..policy.Rules] };
        var second = JobRunner.Build(catalog, Planner.Analyze(catalog, renamed), output, cache, processor: cpu);
        Assert.Equal(2, second.Reused); Assert.NotEqual(first.Manifest.PolicyHash, second.Manifest.PolicyHash);
        Assert.Equal(first.Manifest.Files.Select(e => (e.Recipe, e.OutputHash)), second.Manifest.Files.Select(e => (e.Recipe, e.OutputHash)));
        // Anything that changes the bytes changes the recipe.
        var smaller = policy with { Rules = [new("*", 4, RebuildMips: true, Semantic: TextureSemantic.LinearData)] };
        Assert.Equal(0, JobRunner.Build(catalog, Planner.Analyze(catalog, smaller), output, cache, processor: cpu).Reused);
        var semantic = policy with { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.SrgbColor)] };
        Assert.Equal(0, JobRunner.Build(catalog, Planner.Analyze(catalog, semantic), output, cache, processor: cpu).Reused);
        Assert.Equal(2, JobRunner.Build(catalog, Planner.Analyze(catalog, policy), output, cache, processor: cpu).Reused);
    }
    [Fact] public void PruningRemovesOnlyOlderLayoutFilesAndEntriesNoBuildUsedRecently()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("source/b.dds", Fixture.Dds(32, 32));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var cpu = new PenguTEX.Imaging.CpuMipProcessor();
        var plan = Planner.Analyze(catalog, Small with { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        string output = Path.Combine(f.Root, "output"), cache = f.Dir("cache"), store = CacheStore.Folder(cache);
        JobRunner.Build(catalog, plan, output, cache, processor: cpu); Assert.Equal(4, Directory.GetFiles(store).Length);
        string hex = new('A', 64), legacy = f.File("cache/" + hex + ".dds", new byte[100]), legacyMeta = f.File("cache/" + hex + ".dds.json", new byte[10]);
        string journal = f.File("cache/job-0123456789ABCDEF0123.json", new byte[20]), stray = f.File("cache/" + hex + ".dds." + new string('b', 32) + ".tmp", new byte[5]);
        string mine = f.File("cache/notes.txt", new byte[7]), foreign = f.File("cache/v2/keep.dds", new byte[9]), other = f.File("cache/saves/" + hex + ".dds", new byte[3]);
        string fresh = f.File("cache/v2/" + hex + ".dds." + new string('c', 32) + ".tmp", new byte[4]);
        var preview = CacheStore.Prune(cache, dryRun: true);
        Assert.True(preview.DryRun); Assert.Equal(4, preview.Files); Assert.Equal(135, preview.Bytes); Assert.Equal(5, preview.KeptFiles); Assert.Equal(2, preview.OtherFiles);
        Assert.True(File.Exists(legacy)); Assert.Equal(preview with { DryRun = false }, CacheStore.Prune(cache));
        Assert.All(new[] { legacy, legacyMeta, journal, stray }, path => Assert.False(File.Exists(path)));
        Assert.All(new[] { mine, foreign, other, fresh }, path => Assert.True(File.Exists(path)));
        Assert.Equal(2, JobRunner.Build(catalog, plan, output, cache, processor: cpu).Reused);
        // Entries age by their last use: a reused entry survives, the other one and the stale temporary go.
        var later = DateTime.UtcNow.AddDays(40); string[] metadata = Directory.GetFiles(store, "*.dds.json");
        File.SetLastWriteTimeUtc(metadata[0], later.AddDays(-1));
        var aged = CacheStore.Prune(cache, 30, now: later); Assert.Equal(3, aged.Files); Assert.Equal(2, aged.KeptFiles);
        Assert.True(File.Exists(metadata[0])); Assert.True(File.Exists(metadata[0][..^5])); Assert.False(File.Exists(metadata[1])); Assert.False(File.Exists(fresh));
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache, processor: cpu).Reused);
        Assert.Equal(4, CacheStore.Prune(cache, 0).Files); Assert.True(File.Exists(foreign)); Assert.True(File.Exists(mine));
        Assert.Equal(new PruneResult(0, 0, 0, 0, 0, false), CacheStore.Prune(Path.Combine(f.Root, "missing")));
        Assert.Throws<IOException>(() => CacheStore.Prune(output)); Assert.Throws<ArgumentException>(() => CacheStore.Prune(cache, -1));
        JobRunner.Verify(output);
    }
    [Fact] public void PublishedOutputCarriesNoStageMarkerAndOlderOutputsWithOneStayValid()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        JobRunner.Build(catalog, plan, output, cache); Assert.False(File.Exists(Path.Combine(output, ".pengutex-stage")));
        File.WriteAllText(Path.Combine(output, ".pengutex-stage"), JobRunner.Product); JobRunner.Verify(output);
        JobRunner.Build(catalog, plan, output, cache); Assert.False(File.Exists(Path.Combine(output, ".pengutex-stage")));
    }
    [Fact] public void ProtectedTexturesCarryNoMaterialHint()
    {
        using var f = new Fixture(); f.File("source/textures/interface/a.dds", Fixture.RawDds(mipCount: 1)); f.File("source/textures/armor/b.dds", Fixture.RawDds(mipCount: 1));
        var plan = Planner.Analyze(CatalogLoader.Load(new(Folder: f.Dir("source")), inspectMaterials: true), Policy.Preset("balanced"));
        var ui = plan.Items.Single(i => i.Path.Contains("interface")); Assert.Equal(Decision.Protected, ui.Decision); Assert.Null(ui.Material);
        Assert.DoesNotContain("reviewed rule", ui.Reason);
        Assert.Equal(MaterialStatus.Unbound, plan.Items.Single(i => i.Path.Contains("armor")).Material!.Status);
    }
    [Fact] public void PathRulePatternsMatchAsBefore()
    {
        foreach (var (path, pattern, expected) in new[] { ("textures/Armor/A.dds", "textures/armor/*", true), (@"textures\armor\a.dds", "textures/armor/?.dds", true),
            ("textures/armor/ab.dds", "textures/armor/?.dds", false), ("textures/armor/a.dds", @"textures\armor\*", true), ("textures/x.(1).dds", "textures/x.(?).dds", true),
            ("textures/armor/a.dds", "armor/*", false) })
            for (int i = 0; i < 2; i++) Assert.Equal(expected, Policy.Matches(path, pattern));
    }
    [Fact] public void TypedStatesKeepTheirJsonText()
    {
        string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, JsonFiles.Options);
        Assert.Equal("\"Unsupported binding\"", Json(MaterialStatus.UnsupportedBinding)); Assert.Equal("\"Conflicting bindings\"", Json(MaterialStatus.ConflictingBindings));
        Assert.Equal(MaterialStatus.ConflictingBindings, System.Text.Json.JsonSerializer.Deserialize<MaterialStatus>("\"Conflicting bindings\"", JsonFiles.Options));
        Assert.Equal("Unsupported binding", MaterialStatus.UnsupportedBinding.Text()); Assert.Equal("Resolved", MaterialStatus.Resolved.Text());
        Assert.Equal("\"Error\"", Json(Severity.Error)); Assert.Equal("\"RecoverMipRecords\"", Json(TextureOperation.RecoverMipRecords)); Assert.Equal("\"IncompleteMips\"", Json(ComparisonStatus.IncompleteMips));
        // Manifests written before the change still read, including legacy reference copies.
        var entry = System.Text.Json.JsonSerializer.Deserialize<OutputEntry>("{\"Path\":\"textures/a.dds\",\"Source\":\"m\",\"SourceHash\":\"A\",\"OutputHash\":\"B\",\"Width\":4,\"Height\":4,\"DropMips\":1,\"Bytes\":10,\"Recipe\":\"R\",\"Operation\":\"ReferenceCopy\"}", JsonFiles.Options)!;
        Assert.Equal(TextureOperation.ReferenceCopy, entry.Operation);
        Assert.Equal(TextureOperation.MipCopy, System.Text.Json.JsonSerializer.Deserialize<OutputEntry>("{\"Path\":\"a\",\"Source\":\"m\",\"SourceHash\":\"A\",\"OutputHash\":\"B\",\"Width\":4,\"Height\":4,\"DropMips\":0,\"Bytes\":1,\"Recipe\":\"R\"}", JsonFiles.Options)!.Operation);
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

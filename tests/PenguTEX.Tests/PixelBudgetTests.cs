using BCnEncoder.Encoder;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class PixelBudgetTests
{
    private static void Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!condition()) { Assert.True(DateTime.UtcNow < limit, "Timed out waiting for the budget."); Thread.Sleep(1); }
    }

    [Fact] public void ConcurrentReservationsNeverExceedTheBudget()
    {
        var budget = new PixelBudget(100); long held = 0, peak = 0;
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, worker =>
        {
            var random = new Random(worker);
            for (int i = 0; i < 200; i++)
            {
                int size = random.Next(1, 61);
                using (budget.Reserve(size))
                {
                    long now = Interlocked.Add(ref held, size);
                    long seen; while (now > (seen = Interlocked.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
                    Thread.SpinWait(200); Interlocked.Add(ref held, -size);
                }
            }
        });
        Assert.InRange(peak, 1, 100); Assert.Equal(0, budget.InUse); Assert.Equal(0, budget.Waiting);
    }

    // A waiting reservation on its own thread; xunit forbids blocking on tasks.
    private sealed class Request
    {
        private readonly Thread _thread; public IDisposable? Lease; public Exception? Error;
        public Request(PixelBudget budget, long pixels, CancellationToken ct = default)
        {
            _thread = new Thread(() => { try { Lease = budget.Reserve(pixels, ct); } catch (Exception e) { Error = e; } }) { IsBackground = true };
            _thread.Start();
        }
        public bool Finished(int milliseconds) => _thread.Join(milliseconds);
    }

    [Fact] public void FullSizeRequestRunsAloneAndSmallerLaterRequestsWaitTheirTurn()
    {
        var budget = new PixelBudget(100); var first = budget.Reserve(40);
        var large = new Request(budget, 100); Until(() => budget.Waiting == 1);
        // The later request would fit beside the first one, but may not overtake the waiting full-size request.
        var small = new Request(budget, 10); Until(() => budget.Waiting == 2);
        Assert.False(large.Finished(50)); Assert.False(small.Finished(50)); Assert.Equal(40, budget.InUse);
        first.Dispose(); Assert.True(large.Finished(10000)); Assert.Equal(100, budget.InUse);
        Assert.False(small.Finished(50));
        large.Lease!.Dispose(); Assert.True(small.Finished(10000)); Assert.Equal(10, budget.InUse);
        small.Lease!.Dispose(); small.Lease.Dispose(); Assert.Equal(0, budget.InUse);
    }

    [Fact] public void CancellationLeavesTheQueueAndKeepsAccounting()
    {
        var budget = new PixelBudget(100); var held = budget.Reserve(100);
        using var cancel = new CancellationTokenSource();
        var waiting = new Request(budget, 1, cancel.Token); Until(() => budget.Waiting == 1);
        var next = new Request(budget, 100); Until(() => budget.Waiting == 2);
        cancel.Cancel();
        Assert.True(waiting.Finished(10000)); Assert.IsAssignableFrom<OperationCanceledException>(waiting.Error); Assert.Null(waiting.Lease);
        Assert.Equal(1, budget.Waiting); Assert.Equal(100, budget.InUse);
        held.Dispose(); Assert.True(next.Finished(10000)); next.Lease!.Dispose(); Assert.Equal(0, budget.InUse);
        Assert.ThrowsAny<OperationCanceledException>(() => budget.Reserve(1, new CancellationToken(true)));
        Assert.Equal(0, budget.InUse); Assert.Equal(0, budget.Waiting);
    }

    [Fact] public void RejectsRequestsThatCanNeverFit()
    {
        var budget = new PixelBudget(100);
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Reserve(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Reserve(101));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBudget(0));
        Assert.Equal(Policy.MaximumRebuildPixels, CpuMipProcessor.Budget.Capacity);
    }

    [Fact] public void ConcurrentRebuildsProduceTheSameBytesAsSerialOnes()
    {
        using var f = new Fixture(); var random = new Random(7);
        foreach (int dxgi in new[] { 71, 77, 98 })
            for (int n = 0; n < 8; n++)
            {
                byte[] bytes = Fixture.Dds(32, 32, dxgi, mipCount: 1), rgba = new byte[32 * 32 * 4]; random.NextBytes(rgba);
                using var stream = new MemoryStream(bytes); var info = Dds.Inspect(stream).Info;
                var encoder = new BcEncoder(TexturePreview.Format(info)); encoder.OutputOptions.GenerateMipMaps = false; encoder.Options.IsParallel = false;
                encoder.EncodeToRawBytes(rgba, 32, 32, PixelFormat.Rgba32)[0].CopyTo(bytes, info.HeaderBytes);
                f.File($"source/textures/{dxgi}-{n}.dds", bytes);
            }
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var policy = new Policy { Maximum = 16, Rules = [new("*", 16, RebuildMips: true, Semantic: TextureSemantic.LinearData)] };
        var plan = Planner.Analyze(catalog, policy); Assert.Equal(24, plan.Items.Count(i => i.Decision == Decision.Rebuild));
        var serial = JobRunner.Build(catalog, plan, Path.Combine(f.Root, "serial"), Path.Combine(f.Root, "serial-cache"), 1, processor: new CpuMipProcessor());
        var parallel = JobRunner.Build(catalog, plan, Path.Combine(f.Root, "parallel"), Path.Combine(f.Root, "parallel-cache"), 8, processor: new CpuMipProcessor());
        Assert.Equal(0, parallel.Reused);
        Assert.Equal(serial.Manifest.Files.Select(e => (e.Path, e.OutputHash)), parallel.Manifest.Files.Select(e => (e.Path, e.OutputHash)));
    }
}

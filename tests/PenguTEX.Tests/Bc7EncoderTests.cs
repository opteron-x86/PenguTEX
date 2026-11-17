using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class Bc7EncoderTests
{
    [Theory]
    [InlineData(Bc7Backend.Basic)] [InlineData(Bc7Backend.Fast)]
    public void NativeEdgesAreValidDeterministicAndPreserveConstantAlpha(Bc7Backend backend)
    {
        var encoder = new Bc7Encoder(backend); var decoder = new BcDecoder(); decoder.Options.IsParallel = false;
        foreach (int width in new[] { 1, 2, 3, 4, 17, 255, 257 })
        foreach (int rows in new[] { 1, 2, 3, 4 })
        foreach (byte alpha in new byte[] { 0, 255 })
        {
            byte[] rgba = new byte[width * rows * 4]; new Random(width * rows).NextBytes(rgba);
            for (int at = 3; at < rgba.Length; at += 4) rgba[at] = alpha;
            byte[] blocks = encoder.EncodeStrip(rgba, width, rows);
            Assert.Equal((width + 3) / 4 * 16, blocks.Length);
            Assert.Equal(blocks, encoder.EncodeStrip(rgba, width, rows));
            var decoded = decoder.DecodeRaw(blocks, width, rows, CompressionFormat.Bc7);
            Assert.Equal(width * rows, decoded.Length);
            Assert.All(decoded, pixel => Assert.Equal(alpha, pixel.a));
            if (Environment.GetEnvironmentVariable("PENGUTEX_ORACLE_FIXTURES") is { } fixtures)
            {
                string directory = Path.Combine(fixtures, "bc7-native"); Directory.CreateDirectory(directory);
                byte[] dds = Fixture.Dds(width, rows, mipCount: 1); blocks.CopyTo(dds, 148);
                File.WriteAllBytes(Path.Combine(directory, $"{backend}-{width}-{rows}-{alpha}.dds"), dds);
            }
        }
    }

    [Fact]
    public void ConcurrentNativeCallsDoNotShareMutableParameters()
    {
        byte[] rgba = new byte[259 * 4 * 4]; new Random(44).NextBytes(rgba);
        byte[] basic = new Bc7Encoder(Bc7Backend.Basic).EncodeStrip(rgba, 259, 4);
        byte[] fast = new Bc7Encoder(Bc7Backend.Fast).EncodeStrip(rgba, 259, 4);
        Parallel.For(0, 64, n => Assert.Equal(n % 2 == 0 ? basic : fast,
            new Bc7Encoder(n % 2 == 0 ? Bc7Backend.Basic : Bc7Backend.Fast).EncodeStrip(rgba, 259, 4)));
    }

    [Fact]
    public void RejectsInvalidBuffersBeforeEnteringNativeCode()
    {
        var encoder = new Bc7Encoder(Bc7Backend.Basic);
        Assert.Throws<ArgumentException>(() => encoder.EncodeStrip(new byte[16], 1, 5));
        Assert.Throws<ArgumentException>(() => encoder.EncodeStrip(new byte[15], 1, 4));
        Assert.Throws<ArgumentException>(() => encoder.EncodeStrip([], int.MaxValue, 4));
        Assert.Throws<ArgumentException>(() => encoder.EncodeStrip([], 0, 1));
    }

    [Fact]
    public void BackendChangesSeparateCachesAndRetainedMipsStayIdentical()
    {
        using var f = new Fixture(); byte[] original = ImagingTests.SolidDds(98);
        f.File("source/a.dds", original); f.File("source/b.dds", original);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, new() { Rules = [new("*", 16, Semantic: TextureSemantic.LinearData, CompleteMipChain: true)] });
        string output = f.Dir("output"), cache = f.Dir("cache");
        var basic = new CpuMipProcessor(Bc7Backend.Basic); var fast = new CpuMipProcessor(Bc7Backend.Fast);
        Assert.NotEqual(basic.Identity, fast.Identity);
        Assert.NotEqual(basic.Identity, new CpuMipProcessor(Bc7Backend.Managed).Identity);
        Assert.Equal(0, JobRunner.Build(catalog, plan, output, cache, workers: 2, processor: basic).Reused);
        Assert.Equal(2, JobRunner.Build(catalog, plan, output, cache, workers: 2, processor: basic).Reused);
        Assert.Equal(0, JobRunner.Build(catalog, plan, output, cache, workers: 2, processor: fast).Reused);
        byte[] result = File.ReadAllBytes(Path.Combine(output, "textures/a.dds"));
        Assert.Equal(original[148..], result[148..original.Length]);
        var item = plan.Items[0];
        using var cancelledOutput = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => basic.Process(new MemoryStream(original), cancelledOutput, item, original.Length, new CancellationToken(true)));
        Assert.Single(JobRunner.Verify(output).Files, x => x.Path == "textures/a.dds");
    }
}

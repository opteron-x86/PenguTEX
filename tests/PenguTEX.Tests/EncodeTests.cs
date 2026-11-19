using System.Buffers.Binary;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class EncodeTests
{
    private static Policy Small(int maximum = 64) => Policy.Preset("balanced") with { Maximum = maximum, NormalMaximum = maximum, MaterialMaximum = maximum, HeightMaximum = maximum, CompleteKnownMaterials = false };

    // An uncompressed texture whose levels are smooth gradients, so BC7 error stays small and measurable.
    private static byte[] Smooth(int width, int height, int? mipCount = null, int? dxgi = null, int bits = 32, byte alpha = 0)
    {
        byte[] data = Fixture.RawDds(width, height, bits: bits, dxgi: dxgi, mipCount: mipCount);
        var info = Dds.Inspect(new MemoryStream(data)).Info; int at = info.HeaderBytes, stride = info.BitsPerPixel / 8;
        for (int level = 0; level < info.MipCount; level++)
        {
            int w = Math.Max(1, width >> level), h = Math.Max(1, height >> level);
            for (int y = 0; y < h; y++, at += (int)info.RowBytes(level))
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < stride; c++)
                        data[at + x * stride + c] = c switch
                        {
                            0 => (byte)(40 + 160 * x / Math.Max(1, w - 1)), 1 => (byte)(200 - 120 * y / Math.Max(1, h - 1)),
                            2 => (byte)(90 + level * 20), _ => alpha != 0 ? alpha : (byte)(60 + 160 * x / Math.Max(1, w - 1)), // alpha follows red, as in most real textures
                        };
        }
        return data;
    }

    private static void AssertLevelsClose(byte[] source, int sourceLevel, byte[] output, int outputLevel, int tolerance)
    {
        var a = TexturePreview.Decode(new MemoryStream(source), sourceLevel); var b = TexturePreview.Decode(new MemoryStream(output), outputLevel);
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        // Four independent steep gradients in one 4×4 block are hard for any block format: bound the mean tightly and the worst loosely.
        var errors = a.Rgba.Zip(b.Rgba, (x, y) => Math.Abs(x - y)).ToArray();
        Assert.True(errors.Max() <= 2 * tolerance && errors.Average() <= tolerance / 3.0,
            $"level {outputLevel}: worst channel error {errors.Max()}, mean {errors.Average():F2}");
    }

    [Fact] public void OnlyEightBitUncompressedLayoutsAreCompressed()
    {
        string? Target(byte[] dds) => Policy.CompressionTarget(Dds.Inspect(new MemoryStream(dds)).Info);
        Assert.Equal("BC7", Target(Fixture.RawDds()));                       // legacy RGBA8
        Assert.Equal("BC7", Target(Fixture.RawDds(bits: 24)));               // legacy RGB8
        Assert.Equal("BC7", Target(Fixture.RawDds(dxgi: 28)));
        Assert.Equal("BC7", Target(Fixture.RawDds(dxgi: 87)));
        Assert.Equal("BC7", Target(Fixture.RawDds(bits: 16, dxgi: 49)));     // R8G8
        Assert.Equal("BC4", Target(Fixture.RawDds(bits: 8, dxgi: 61)));      // R8
        Assert.Null(Target(Fixture.RawDds(bits: 8)));                        // luminance: no block format samples the same
        Assert.Null(Target(Fixture.RawDds(bits: 8, dxgi: 65)));              // alpha only
        Assert.Null(Target(Fixture.RawDds(bits: 16)));                       // 565
        Assert.Null(Target(Fixture.RawDds(bits: 64, dxgi: 10)));             // half float
        Assert.Null(Target(Fixture.Dds()));                                  // already compressed
        var srgb = Dds.Encoded(Dds.Inspect(new MemoryStream(Fixture.RawDds(dxgi: 29))).Info, "BC7");
        Assert.Equal((99, true), (srgb.Dxgi, srgb.Srgb));
        Assert.Throws<ArgumentException>(() => Dds.Encoded(Dds.Inspect(new MemoryStream(Fixture.RawDds(dxgi: 29))).Info, "BC4"));
        Assert.True(Policy.Preset("quality").EncodeUncompressed); Assert.False(new Policy().EncodeUncompressed); Assert.False(Policy.Preset("compact").EncodeFaceTints);
    }

    [Fact] public void PlansEncodeKeptAndReducedTexturesAndLeaveTheRestAlone()
    {
        using var f = new Fixture();
        f.File("source/textures/a.dds", Smooth(32, 16)); f.File("source/textures/big.dds", Smooth(64, 64));
        f.File("source/textures/interface/ui.dds", Smooth(32, 16)); f.File("source/textures/bc.dds", Fixture.Dds(16, 16));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, Small(16)); PlanItem Item(string name) => plan.Items.Single(i => i.Path.EndsWith(name));
        var a = Item("/a.dds"); Assert.Equal((Decision.Rebuild, TextureOperation.Encode, "BC7"), (a.Decision, a.Operation, a.OutputFormat));
        Assert.Equal((16, 8, 1), (a.TargetWidth, a.TargetHeight, a.DropMips)); Assert.StartsWith("Compress uncompressed Raw32 to BC7", a.Reason);
        Assert.Equal(Dds.Encoded(a.Input!, "BC7").PayloadBytesFrom(1), a.OutputPayload); Assert.True(a.SavedBytes > a.Input!.PayloadBytes * 7 / 8);
        var big = Item("big.dds"); Assert.Equal((16, 2), (big.TargetWidth, big.DropMips));
        Assert.Equal(Decision.Protected, Item("ui.dds").Decision); Assert.Equal(Decision.Keep, Item("bc.dds").Decision);
        Assert.Equal(0, plan.Coverage.IncompleteMips);
        var off = Planner.Analyze(catalog, Small(16) with { EncodeUncompressed = false });
        Assert.Equal(TextureOperation.MipCopy, off.Items.Single(i => i.Path.EndsWith("/a.dds")).Operation);
    }

    [Theory] [InlineData(32, null, 98)] [InlineData(32, 29, 99)] [InlineData(24, null, 98)]
    public void EncodedOutputKeepsEveryRetainedLevelAndItsColourSpace(int bits, int? dxgi, int expectedDxgi)
    {
        using var f = new Fixture(); byte[] source = Smooth(64, 32, dxgi: dxgi, bits: bits); f.File("source/textures/a.dds", source);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small(32));
        var item = Assert.Single(plan.Items); Assert.Equal(TextureOperation.Encode, item.Operation);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache"); var cpu = new CpuMipProcessor();
        var built = JobRunner.Build(catalog, plan, output, cache, processor: cpu);
        var entry = Assert.Single(built.Manifest.Files); Assert.Equal((TextureOperation.Encode, "BC7"), (entry.Operation, entry.OutputFormat));
        byte[] result = File.ReadAllBytes(Path.Combine(output, "textures/a.dds"));
        var info = Dds.Inspect(new MemoryStream(result)).Info;
        Assert.Equal(("BC7", expectedDxgi, 32, 16, 6), (info.Format, info.Dxgi, info.Width, info.Height, info.MipCount));
        Assert.Equal(item.OutputPayload, info.PayloadBytes);
        for (int level = 0; level < info.MipCount; level++) AssertLevelsClose(source, level + 1, result, level, 12);
        if (Environment.GetEnvironmentVariable("PENGUTEX_ORACLE_FIXTURES") is { } fixtures)
        {
            // An independent decoder compares each encoded level with the source level one step larger.
            string directory = Path.Combine(fixtures, "encoded"); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"{bits}-{dxgi ?? 0}.source.dds"), source);
            File.WriteAllBytes(Path.Combine(directory, $"{bits}-{dxgi ?? 0}.bc7.dds"), result);
        }
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache, processor: cpu).Reused);
        Assert.Single(JobRunner.Verify(output).Files);
    }

    [Fact] public void SingleChannelRedBecomesBc4()
    {
        using var f = new Fixture(); byte[] source = Smooth(32, 32, dxgi: 61, bits: 8); f.File("source/textures/mask.dds", source);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Small(32));
        Assert.Equal("BC4", Assert.Single(plan.Items).OutputFormat);
        var built = JobRunner.Build(catalog, plan, Path.Combine(f.Root, "output"), Path.Combine(f.Root, "cache"), processor: new CpuMipProcessor());
        byte[] result = File.ReadAllBytes(Path.Combine(built.Output, "textures/mask.dds")); var info = Dds.Inspect(new MemoryStream(result)).Info;
        Assert.Equal(("BC4", 80, 6), (info.Format, info.Dxgi, info.MipCount));
        var a = TexturePreview.Decode(new MemoryStream(source)); var b = TexturePreview.Decode(new MemoryStream(result));
        Assert.True(Enumerable.Range(0, a.Width * a.Height).Max(i => Math.Abs(a.Rgba[i * 4] - b.Rgba[i * 4])) <= 4);
    }

    [Fact] public void MissingLevelsAreCompletedInTheNewFormat()
    {
        using var f = new Fixture(); byte[] source = Smooth(32, 32, mipCount: 2); f.File("source/textures/a.dds", source);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, Small(32) with { Rules = [new("*", CompleteMipChain: true, Semantic: TextureSemantic.SrgbColor)] });
        var item = Assert.Single(plan.Items); Assert.Equal((TextureOperation.CompleteMips, "BC7"), (item.Operation, item.OutputFormat));
        Assert.Contains("encoded to BC7", item.Reason);
        var built = JobRunner.Build(catalog, plan, Path.Combine(f.Root, "output"), Path.Combine(f.Root, "cache"), processor: new CpuMipProcessor());
        byte[] result = File.ReadAllBytes(Path.Combine(built.Output, "textures/a.dds")); var info = Dds.Inspect(new MemoryStream(result)).Info;
        Assert.Equal(("BC7", 6, item.OutputPayload), (info.Format, info.MipCount, info.PayloadBytes));
        AssertLevelsClose(source, 0, result, 0, 12); AssertLevelsClose(source, 1, result, 1, 12);
        Assert.Equal(0, plan.Coverage.IncompleteMips);
    }

    [Fact] public void FaceTintsCanBeCompressedAtFullSizeWhileCharactersStayProtected()
    {
        using var f = new Fixture();
        f.File("source/textures/actors/character/facegendata/facetint/skyrim.esm/00012345.dds", Smooth(64, 64));
        f.File("source/textures/actors/character/female/body.dds", Smooth(64, 64));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var guarded = Small(16) with { ProtectCharacters = true };
        Assert.All(Planner.Analyze(catalog, guarded).Items, i => Assert.Equal(Decision.Protected, i.Decision));
        var plan = Planner.Analyze(catalog, guarded with { EncodeFaceTints = true });
        var tint = plan.Items.Single(i => i.Path.Contains("facetint"));
        Assert.Equal((TextureOperation.Encode, 64, 0), (tint.Operation, tint.TargetWidth, tint.DropMips)); Assert.StartsWith("Face tint", tint.Reason);
        Assert.Equal(Decision.Protected, plan.Items.Single(i => i.Path.Contains("body")).Decision);
        // Without character protection a face tint is an ordinary texture: compressed and resized.
        var open = Planner.Analyze(catalog, Small(16)).Items.Single(i => i.Path.Contains("facetint"));
        Assert.Equal((TextureOperation.Encode, 16), (open.Operation, open.TargetWidth));
    }

    [Fact] public void ComparisonsAndCacheUseTheOutputFormat()
    {
        using var f = new Fixture(); byte[] source = Smooth(32, 32); f.File("source/a.dds", source); f.File("reference/a.dds", source);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")) { ReferenceFolder = f.Dir("reference") });
        var plan = Planner.Analyze(catalog, Small(32));
        var row = Assert.Single(plan.Comparison!.Items); Assert.Equal(ComparisonStatus.Smaller, row.Status);
        Assert.Equal("BC7", row.Proposed!.Format); Assert.Equal(plan.Items[0].OutputPayload, row.ProposedPayload);
        string cache = Path.Combine(f.Root, "cache"); var cpu = new CpuMipProcessor();
        var encoded = JobRunner.Build(catalog, plan, Path.Combine(f.Root, "one"), cache, processor: cpu);
        var kept = JobRunner.Build(catalog, Planner.Analyze(catalog, Small(32) with { EncodeUncompressed = false, Rules = [new("*", 32, RebuildMips: true, Semantic: TextureSemantic.SrgbColor)] }),
            Path.Combine(f.Root, "two"), cache, processor: cpu);
        Assert.Equal(0, kept.Reused); Assert.NotEqual(encoded.Manifest.Files[0].Recipe, kept.Manifest.Files[0].Recipe);
        Assert.Null(kept.Manifest.Files[0].OutputFormat);
    }

    [Fact] public void CutoutCoverageSurvivesCompression()
    {
        using var f = new Fixture(); byte[] source = Smooth(64, 64);
        var info = Dds.Inspect(new MemoryStream(source)).Info; int at = info.HeaderBytes;
        // A hard-edged cutout: alpha 0 or 255 in irregular patches.
        for (int level = 0; level < info.MipCount; level++)
            for (int i = 0; i < Math.Max(1, 64 >> level) * Math.Max(1, 64 >> level); i++, at += 4) source[at + 3] = (byte)(((i * 7) ^ (i / 13)) % 5 < 2 ? 0 : 255);
        f.File("source/textures/leaf.dds", source); f.File("source/meshes/leaf.nif", Fixture.Mesh(0, "textures/leaf.dds", alpha: 512 | (4 << 10), threshold: 127));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")), inspectMaterials: true);
        var item = Assert.Single(Planner.Analyze(catalog, Small(64)).Items);
        Assert.Equal((TextureOperation.Encode, MaterialRole.Cutout, 128), (item.Operation, item.Material!.Role, item.AlphaCutoff));
        using var output = new MemoryStream();
        new CpuMipProcessor().Process(new MemoryStream(source), output, item, source.Length, TestContext.Current.CancellationToken);
        var before = TexturePreview.Decode(new MemoryStream(source)); var after = TexturePreview.Decode(new MemoryStream(output.ToArray()));
        int Visible(PixelImage p) => Enumerable.Range(0, p.Width * p.Height).Count(i => p.Rgba[i * 4 + 3] >= 128);
        Assert.InRange(Math.Abs(Visible(before) - Visible(after)), 0, 64 * 64 / 100);
    }
    [Fact] public void ThinCutoutsKeepEveryVisibleTexelInSmallLevels()
    {
        // One-texel wires with alpha just above the cutoff: lossy alpha would erode them.
        using var f = new Fixture(); byte[] source = Smooth(32, 32);
        var info = Dds.Inspect(new MemoryStream(source)).Info; int at = info.HeaderBytes;
        for (int level = 0; level < info.MipCount; level++)
        {
            int w = Math.Max(1, 32 >> level);
            for (int i = 0; i < w * w; i++, at += 4) source[at + 3] = (byte)((i % w) % 3 == 0 || (i / w) % 5 == 0 ? 140 : 90);
        }
        f.File("source/textures/cage.dds", source); f.File("source/meshes/cage.nif", Fixture.Mesh(0, "textures/cage.dds", alpha: 512 | (4 << 10), threshold: 127));
        var item = Assert.Single(Planner.Analyze(CatalogLoader.Load(new(Folder: f.Dir("source")), inspectMaterials: true), Small(64)).Items);
        using var output = new MemoryStream();
        new CpuMipProcessor().Process(new MemoryStream(source), output, item, source.Length, TestContext.Current.CancellationToken);
        for (int level = 0; level < info.MipCount; level++)
        {
            var before = TexturePreview.Decode(new MemoryStream(source), level); var after = TexturePreview.Decode(new MemoryStream(output.ToArray()), level);
            for (int i = 0; i < before.Width * before.Height; i++) Assert.Equal(before.Rgba[i * 4 + 3] >= 128, after.Rgba[i * 4 + 3] >= 128);
        }
    }
}

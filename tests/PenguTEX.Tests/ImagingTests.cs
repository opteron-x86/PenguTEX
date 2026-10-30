using BCnEncoder.Encoder;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class ImagingTests
{
    public static byte[] SolidDds(int dxgi, string? cc = null)
    {
        var bytes = Fixture.Dds(dxgi: dxgi, fourCC: cc, mipCount: 1);
        using var stream = new MemoryStream(bytes); var info = Dds.Inspect(stream).Info;
        byte[] rgba = Enumerable.Range(0, 16 * 16).SelectMany(_ => new byte[] { 64, 128, 192, 0 }).ToArray();
        var encoder = new BcEncoder(TexturePreview.Format(info)); encoder.OutputOptions.GenerateMipMaps = false;
        encoder.Options.IsParallel = false;
        encoder.EncodeToRawBytes(rgba, 16, 16, PixelFormat.Rgba32)[0].CopyTo(bytes, info.HeaderBytes); return bytes;
    }
    [Theory]
    [InlineData(71, null)] [InlineData(72, null)] [InlineData(74, null)] [InlineData(77, null)]
    [InlineData(80, null)] [InlineData(83, null)] [InlineData(98, null)] [InlineData(99, null)]
    [InlineData(71, "DXT1")] [InlineData(74, "DXT3")] [InlineData(77, "DXT5")] [InlineData(83, "ATI2")]
    public void RebuildPreservesFormatAndGeneratesEveryMip(int dxgi, string? cc)
    {
        using var f = new Fixture(); var data = SolidDds(dxgi, cc); f.File("source/test.dds", data);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var semantic = dxgi is 72 or 99 ? TextureSemantic.SrgbColor : TextureSemantic.LinearData;
        var policy = new Policy { Maximum = 8, Rules = [new("*", 8, RebuildMips: true, Semantic: semantic)] };
        var plan = Planner.Analyze(catalog, policy); var item = Assert.Single(plan.Items); Assert.Equal(Decision.Rebuild, item.Decision);
        string output = Path.Combine(f.Root, "output"), cache = Path.Combine(f.Root, "cache");
        var result = JobRunner.Build(catalog, plan, output, cache, processor: new CpuMipProcessor());
        var entry = Assert.Single(result.Manifest.Files); Assert.Equal("Rebuild", entry.Operation); Assert.Equal(semantic, entry.Semantic);
        using var file = File.OpenRead(Path.Combine(output, "textures/test.dds")); var info = Dds.Inspect(file).Info;
        Assert.Equal(8, info.Width); Assert.Equal(4, info.MipCount); Assert.Equal(cc is null ? dxgi : 0, info.Dxgi);
        Assert.Equal(item.OutputPayload, info.PayloadBytes);
        if (Environment.GetEnvironmentVariable("PENGUTEX_ORACLE_FIXTURES") is { } fixtures)
        {
            Directory.CreateDirectory(fixtures);
            System.IO.File.Copy(Path.Combine(output, "textures/test.dds"), Path.Combine(fixtures, $"bc-{dxgi}-{cc ?? "dx10"}.dds"), true);
        }
        for (int mip = 0; mip < info.MipCount; mip++) { file.Position = 0; Assert.Equal(Math.Max(1, 8 >> mip), TexturePreview.Decode(file, mip).Width); }
        Assert.Single(JobRunner.Verify(output).Files);
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache, processor: new CpuMipProcessor()).Reused);
    }
    [Fact]
    public void ColorFilteringUsesLinearLightAndDataKeepsZeroAlphaRgb()
    {
        var image = new PixelImage([0,0,0,0, 255,255,255,0], 2, 1);
        var color = MipFilter.Downsample(image, TextureSemantic.SrgbColor);
        Assert.InRange(color.Rgba[0], 187, 188); Assert.Equal(0, color.Rgba[3]);
        var data = MipFilter.Downsample(image, TextureSemantic.LinearData);
        Assert.Equal(new byte[] {128,128,128,0}, data.Rgba);
        using var f = new Fixture(); f.File("source/test.dds", SolidDds(98)); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, new() { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        using var input = catalog.Assets[0].Open(); using var output = new MemoryStream();
        new CpuMipProcessor().Process(input, output, plan.Items[0], catalog.Assets[0].Length, default); output.Position = 0;
        var decoded = TexturePreview.Decode(output); Assert.InRange(decoded.Rgba[0], 60, 68); Assert.InRange(decoded.Rgba[2], 188, 196); Assert.Equal(0, decoded.Rgba[3]);
    }
    [Fact]
    public void NormalDirectionsNormalizeWithoutChangingGlossFiltering()
    {
        var image = new PixelImage([255,128,128,0, 128,255,128,255], 2, 1);
        var filtered = MipFilter.Downsample(image, TextureSemantic.NormalRgb);
        double x = filtered.Rgba[0] / 127.5 - 1, y = filtered.Rgba[1] / 127.5 - 1, z = filtered.Rgba[2] / 127.5 - 1;
        double dot = (x + y) / Math.Sqrt(2 * (x*x + y*y + z*z));
        Assert.InRange(Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI, 0, 2); Assert.Equal(128, filtered.Rgba[3]);
        var rg = MipFilter.Downsample(new([128,128,0,40, 128,128,0,80], 2, 1), TextureSemantic.NormalRg);
        Assert.InRange(rg.Rgba[2], 254, 255); Assert.Equal(60, rg.Rgba[3]);
    }
    [Fact]
    public void AreaFilterIncludesOddEdgesAndCoverageIsOnlyForOpacity()
    {
        var odd = new PixelImage([0,0,0,0, 0,0,0,0, 255,255,255,255], 3, 1);
        Assert.Equal(new byte[] {85,85,85,85}, MipFilter.Downsample(odd, TextureSemantic.LinearData).Rgba);
        var cutout = new PixelImage([255,0,0,255, 0,0,255,0, 255,0,0,0, 0,0,255,0], 4, 1);
        var opacity = MipFilter.Downsample(cutout, TextureSemantic.OpacitySrgb, .5);
        Assert.Equal(.5, MipFilter.Coverage(opacity)); Assert.Equal(255, opacity.Rgba[0]); Assert.Equal(0, opacity.Rgba[2]);
    }
    [Fact]
    public void AmbiguousSignedPremultipliedAndMismatchedRulesStayUnchanged()
    {
        Assert.Throws<ArgumentException>(() => new Policy { Rules = [new("*", RebuildMips: true)] }.Validate());
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds(dxgi: 84)); f.File("source/b.dds", Fixture.Dds(fourCC: "DXT2"));
        f.File("source/c.dds", Fixture.Dds(dxgi: 99)); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, new() { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.NormalRg)] });
        Assert.All(plan.Items, i => { Assert.Equal(Decision.Unsupported, i.Decision); Assert.Equal(0, i.SavedBytes); });
        f.File("source/no-mips.dds", Fixture.Dds(mipCount: 1));
        var defaultPlan = Planner.Analyze(CatalogLoader.Load(new(Folder: f.Dir("source"))), new() { Maximum = 8 });
        Assert.Equal(Decision.Keep, defaultPlan.Items.Single(i => i.Path.EndsWith("no-mips.dds")).Decision);
    }
    [Fact]
    public void ChangingSourceNeverCommitsPoisonedCache()
    {
        using var f = new Fixture(); byte[] original = Fixture.Dds(), changed = (byte[])original.Clone(); changed[^20] ^= 99;
        f.File("source/a.dds", original); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, new() { Maximum = 8 }); int opens = 0;
        var unstable = catalog with { Assets = [catalog.Assets[0] with { Open = () => new MemoryStream(++opens == 1 ? original : changed) }] };
        string cache = Path.Combine(f.Root, "cache"), output = Path.Combine(f.Root, "output");
        Assert.ThrowsAny<Exception>(() => JobRunner.Build(unstable, plan, output, cache));
        Assert.Empty(Directory.GetFiles(cache, "*.dds")); Assert.False(Directory.Exists(output));
        Assert.Equal(0, JobRunner.Build(catalog, plan, output, cache).Reused);
    }
}

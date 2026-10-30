using System.Buffers.Binary;
using BCnEncoder.Decoder;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class RawImagingTests
{
    [Theory] [InlineData(8, 1, null)] [InlineData(16, 4, null)] [InlineData(24, 4, null)]
    [InlineData(32, 1, null)] [InlineData(32, 1, 28)] [InlineData(32, 1, 87)]
    [InlineData(16, 1, 49)] [InlineData(8, 1, 61)] [InlineData(8, 1, 65)]
    public void RawRebuildingRetainsChannelsHeadersAndRowLayout(int bits, int alignment, int? dxgi)
    {
        using var f = new Fixture(); var bytes = Fixture.RawDds(9, 5, bits, alignment, dxgi, 1);
        f.File("source/raw.dds", bytes); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, new() { Rules = [new("*", 4, RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        var expected = MipFilter.Downsample(TexturePreview.Decode(new MemoryStream(bytes)), TextureSemantic.LinearData);
        string output = f.Dir("output");
        var result = JobRunner.Build(catalog, plan, output, f.Dir("cache"), processor: new CpuMipProcessor());
        var data = System.IO.File.ReadAllBytes(Path.Combine(output, "textures/raw.dds"));
        Assert.Equal(bytes[76..108], data[76..108]); if (dxgi.HasValue) Assert.Equal(bytes[128..148], data[128..148]);
        var info = Dds.Inspect(new MemoryStream(data)).Info; Assert.Equal(4, info.Width); Assert.Equal(2, info.Height); Assert.Equal(3, info.MipCount);
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)) & (8u | 0x80000u));
        Assert.Equal((uint)info.RowBytes(0), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20)));
        var actual = TexturePreview.Decode(new MemoryStream(data));
        for (int i = 0; i < expected.Rgba.Length; i++) Assert.InRange(Math.Abs(actual.Rgba[i] - expected.Rgba[i]), 0, bits == 16 && dxgi is null ? 4 : 0);
        Assert.Single(JobRunner.Verify(output).Files);
        Assert.Contains("raw", result.Manifest.Files[0].Processor);
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, f.Dir("cache"), processor: new CpuMipProcessor()).Reused);
    }
    [Fact] public void IndependentDdsReaderSeesRebuiltRgbaMipsAndZeroAlphaData()
    {
        byte[] data = Fixture.RawDds(8, 4, dxgi: 28, mipCount: 1);
        for (int p = 148; p < data.Length; p += 4) { data[p] = 64; data[p + 1] = 128; data[p + 2] = 192; data[p + 3] = 0; }
        using var f = new Fixture(); f.File("source/a.dds", data); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, new() { Rules = [new("*", 4, RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        using var output = new MemoryStream(); new CpuMipProcessor().Process(new MemoryStream(data), output, plan.Items[0], data.Length, default);
        output.Position = 0; var mips = new BcDecoder().DecodeAllMipMaps2D(output);
        if (Environment.GetEnvironmentVariable("PENGUTEX_ORACLE_FIXTURES") is { } fixtures)
        { Directory.CreateDirectory(fixtures); System.IO.File.WriteAllBytes(Path.Combine(fixtures, "raw-rgba.dds"), output.ToArray()); }
        Assert.Equal(3, mips.Length);
        foreach (var mip in mips) foreach (var pixel in mip.Span)
        { Assert.Equal(64, pixel.r); Assert.Equal(128, pixel.g); Assert.Equal(192, pixel.b); Assert.Equal(0, pixel.a); }
    }
    [Theory] [InlineData(71)] [InlineData(74)] [InlineData(77)] [InlineData(80)] [InlineData(83)] [InlineData(98)]
    public void StripDecoderMatchesWholeImageAtOddBlockEdges(int dxgi)
    {
        var bytes = Fixture.Dds(7, 9, dxgi, mipCount: 1); using var full = new MemoryStream(bytes);
        var info = Dds.Inspect(full).Info; var expected = new BcDecoder().DecodeRaw(full, 7, 9, TexturePreview.Format(info));
        var actual = TexturePreview.Decode(new MemoryStream(bytes));
        for (int i = 0; i < expected.Length; i++)
        { Assert.Equal(expected[i].r, actual.Rgba[i * 4]); Assert.Equal(expected[i].g, actual.Rgba[i * 4 + 1]); Assert.Equal(expected[i].b, actual.Rgba[i * 4 + 2]); Assert.Equal(expected[i].a, actual.Rgba[i * 4 + 3]); }
    }
    [Fact] public void LargeRebuildLimitAndSemanticRestrictionsAreExplicit()
    {
        var info = Dds.Inspect(new MemoryStream(Fixture.RawDds(dxgi: 28))).Info;
        Assert.Null(Policy.RebuildRestriction(info with { Width = 8192, Height = 8192 }, TextureSemantic.LinearData));
        Assert.Contains("64 million", Policy.RebuildRestriction(info with { Width = 16384, Height = 8192 }, TextureSemantic.LinearData));
        Assert.Contains("precision", Policy.RebuildRestriction(Dds.Inspect(new MemoryStream(Fixture.RawDds(bits: 64, dxgi: 10))).Info, TextureSemantic.LinearData));
        var rg = Dds.Inspect(new MemoryStream(Fixture.RawDds(bits: 16, dxgi: 49))).Info;
        Assert.Null(Policy.RebuildRestriction(rg, TextureSemantic.NormalRg)); Assert.NotNull(Policy.RebuildRestriction(rg, TextureSemantic.NormalRgb));
        Assert.Throws<OperationCanceledException>(() => TexturePreview.Decode(new MemoryStream(Fixture.RawDds()), ct: new CancellationToken(true)));
    }
}

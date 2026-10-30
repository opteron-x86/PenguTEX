using System.Buffers.Binary;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class MipCompletionTests
{
    private static Policy Completion(int maximum = 8, TextureSemantic semantic = TextureSemantic.LinearData) =>
        new() { Rules = [new("*", maximum, Semantic: semantic, CompleteMipChain: true)] };

    [Theory] [InlineData(71)] [InlineData(74)] [InlineData(77)] [InlineData(80)] [InlineData(83)] [InlineData(98)] [InlineData(99)]
    public void CompletesCompressedMipsAndPreservesEveryAvailableBlock(int dxgi)
    {
        using var f = new Fixture(); var data = ImagingTests.SolidDds(dxgi); f.File("source/a.dds", data);
        var semantic = dxgi == 99 ? TextureSemantic.SrgbColor : TextureSemantic.LinearData;
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var fullPlan = Planner.Analyze(catalog, new() { Rules = [new("*", 16, RebuildMips: true, Semantic: semantic)] });
        using var full = new MemoryStream(); new CpuMipProcessor().Process(new MemoryStream(data), full, fullPlan.Items[0], data.Length, default);
        var info = Dds.Inspect(new MemoryStream(full.ToArray())).Info;
        byte[] partial = full.ToArray()[..(info.HeaderBytes + (int)info.RemovedBytes(2))];
        BinaryPrimitives.WriteUInt32LittleEndian(partial.AsSpan(28), 2); f.File("source/a.dds", partial);
        f.File("reference/a.dds", full.ToArray());
        catalog = CatalogLoader.Load(new(Folder: f.Dir("source")) { ReferenceFolder = f.Dir("reference") });
        var plan = Planner.Analyze(catalog, Completion(8, semantic) with { FitReference = true });
        var item = Assert.Single(plan.Items); Assert.True(item.PreserveExistingMips); Assert.Equal(1, item.DropMips);
        Assert.False(plan.Comparison!.HasUnresolved);
        string output = f.Dir("output"), cache = f.Dir("cache");
        var built = JobRunner.Build(catalog, plan, output, cache, processor: new CpuMipProcessor());
        Assert.Equal("CompleteMips", built.Manifest.Files[0].Operation); Assert.True(built.Manifest.OriginalsOnly);
        byte[] result = File.ReadAllBytes(Path.Combine(output, "textures/a.dds"));
        Assert.Equal(partial[(info.HeaderBytes + (int)info.MipBytes(0))..], result[info.HeaderBytes..(info.HeaderBytes + (int)info.MipBytes(1))]);
        Assert.Equal(4, Dds.Inspect(new MemoryStream(result)).Info.MipCount);
        Assert.Equal(1, JobRunner.Build(catalog, plan, output, cache, processor: new CpuMipProcessor()).Reused);
        if (Environment.GetEnvironmentVariable("PENGUTEX_ORACLE_FIXTURES") is { } fixtures)
        { Directory.CreateDirectory(fixtures); File.WriteAllBytes(Path.Combine(fixtures, $"complete-{dxgi}.dds"), result); }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RawCompletionStreamsOddAlignedLevelsAndFiltersOnlyMissingLevels(int drop)
    {
        using var f = new Fixture(); byte[] original = Fixture.RawDds(17, 9, 24, 4, mipCount: 2);
        f.File("source/a.dds", original); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, Completion(drop == 0 ? 32 : 16 >> drop));
        var item = plan.Items[0]; Assert.Equal(drop, item.DropMips);
        using var result = new MemoryStream();
        new CpuMipProcessor().Process(new ForwardOnly(original), result, item, original.Length, default);
        byte[] output = result.ToArray(); var info = Dds.Inspect(new MemoryStream(output)).Info;
        Assert.Equal(1 + (int)Math.Log2(Math.Max(info.Width, info.Height)), info.MipCount);
        if (drop < 2) Assert.Equal(original[(128 + (int)item.Input!.RemovedBytes(drop))..], output[128..(128 + original.Length - 128 - (int)item.Input.RemovedBytes(drop))]);
        var expected = TexturePreview.Decode(new MemoryStream(original), 1);
        var level = item.Input!.MipCount - drop;
        if (level <= 0) level = 0;
        expected = MipFilter.Downsample(expected, TextureSemantic.LinearData);
        var actual = TexturePreview.Decode(new MemoryStream(output), level);
        Assert.Equal(expected.Width, actual.Width); Assert.Equal(expected.Height, actual.Height); Assert.Equal(expected.Rgba, actual.Rgba);
    }

    [Fact] public void CompleteChainsUseCopyingAndCompletionStillRequiresSemantics()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        Assert.Equal(Decision.Reduce, Planner.Analyze(catalog, Completion()).Items[0].Decision);
        Assert.Equal(Decision.Keep, Planner.Analyze(catalog, Completion(16)).Items[0].Decision);
        Assert.Throws<ArgumentException>(() => new Policy { Rules = [new("*", CompleteMipChain: true)] }.Validate());
        Assert.Throws<ArgumentException>(() => new Policy { Rules = [new("*", RebuildMips: true, Semantic: TextureSemantic.LinearData, CompleteMipChain: true)] }.Validate());
        var info = Dds.Inspect(new MemoryStream(Fixture.RawDds())).Info with { Width = 16384, Height = 8192, MipCount = 4 };
        Assert.Null(Policy.RebuildRestriction(info, TextureSemantic.LinearData, preserveExistingMips: true));
        Assert.NotNull(Policy.RebuildRestriction(info, TextureSemantic.LinearData));
    }

    [Fact] public void CancellationPreservesThePublishedOutput()
    {
        using var f = new Fixture(); byte[] bytes = Fixture.RawDds(mipCount: 1); f.File("source/a.dds", bytes);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source"))); var plan = Planner.Analyze(catalog, Completion());
        string output = f.Dir("output"), cache = f.Dir("cache");
        JobRunner.Build(catalog, plan, output, cache, processor: new CpuMipProcessor());
        string before = File.ReadAllText(Path.Combine(output, JobRunner.ManifestName));
        Assert.ThrowsAny<OperationCanceledException>(() => JobRunner.Build(catalog, plan, output, cache, ct: new CancellationToken(true), processor: new CpuMipProcessor()));
        Assert.Equal(before, File.ReadAllText(Path.Combine(output, JobRunner.ManifestName)));
    }

    private sealed class ForwardOnly(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}

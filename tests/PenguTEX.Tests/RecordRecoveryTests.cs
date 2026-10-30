using System.Buffers.Binary;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class RecordRecoveryTests
{
    [Fact] public void SpecialResourcesAreDeliberateCoverageExclusions()
    {
        using var f = new Fixture(); var bytes = Fixture.Dds(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(112), 0x200u);
        f.File("source/cube.dds", bytes); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, new()); Assert.Equal(Decision.Protected, Assert.Single(plan.Items).Decision);
        Assert.Equal(1, plan.Coverage.Protected); Assert.Equal(0, plan.Coverage.Unsupported); Assert.True(plan.Coverage.MeetsPolicy);
    }
    private static Policy Recovery(int max) => new() { Maximum = max, RecoverMipRecords = true };
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RecoversCanonicalBlocksWithSourceProvenanceAndCache(int drop)
    {
        using var f = new Fixture(); byte[] canonical = Fixture.Dds(fourCC: "DXT1"), source = Fixture.MipRecords(canonical);
        f.File("source/a.dds", source); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        Assert.Equal(Decision.Invalid, Planner.Analyze(catalog, new()).Items[0].Decision);
        var plan = Planner.Analyze(catalog, Recovery(16 >> drop)); var item = Assert.Single(plan.Items);
        Assert.Equal("RecoverMipRecords", item.Operation); Assert.Equal(5, item.Input!.MipCount);
        var built = JobRunner.Build(catalog, plan, f.Dir("output"), f.Dir("cache"));
        var entry = Assert.Single(built.Manifest.Files); Assert.True(built.Manifest.OriginalsOnly);
        Assert.Equal("BC1-mip-records16-zero-tail5", entry.SourceRecordLayout); Assert.Equal(1, entry.DeclaredSourceMipCount);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)), entry.SourceHash);
        var output = File.ReadAllBytes(Path.Combine(built.Output, "textures/a.dds"));
        Assert.Equal(canonical[(128 + (int)item.Input.RemovedBytes(drop))..], output[128..]);
        Assert.Null(Dds.Inspect(new MemoryStream(output)).Info.RecordLayout);
        Assert.Equal(1, JobRunner.Build(catalog, plan, f.Dir("output"), f.Dir("cache")).Reused);
        using var forward = new ForwardOnly(source); using var streamed = new MemoryStream();
        Dds.Reduce(forward, streamed, drop, source.Length, expected: item.Input); Assert.Equal(output, streamed.ToArray());
    }
    [Theory] [InlineData(0)] [InlineData(4)] [InlineData(8)] [InlineData(12)] [InlineData(-1)]
    public void RejectsContradictoryDescriptorsAndUnknownFooter(int offset)
    {
        var data = Fixture.MipRecords(Fixture.Dds(fourCC: "DXT1"));
        int at = offset < 0 ? data.Length - 1 : 128 + 128 + offset; data[at] ^= 1;
        Assert.Throws<InvalidDataException>(() => Dds.Inspect(new MemoryStream(data), recoverMipRecords: true));
    }
    [Fact] public void RejectsTruncationOtherFormatsAndTrailingGarbage()
    {
        var bytes = Fixture.MipRecords(Fixture.Dds(fourCC: "DXT1"));
        Assert.Throws<InvalidDataException>(() => Dds.Inspect(new MemoryStream(bytes[..^1]), recoverMipRecords: true));
        Assert.Throws<InvalidDataException>(() => Dds.Inspect(new MemoryStream([..bytes, 0]), recoverMipRecords: true));
        "DXT3"u8.CopyTo(bytes.AsSpan(84)); Assert.Throws<InvalidDataException>(() => Dds.Inspect(new MemoryStream(bytes), recoverMipRecords: true));
        Assert.ThrowsAny<OperationCanceledException>(() => Dds.Inspect(new MemoryStream(Fixture.MipRecords(Fixture.Dds(fourCC: "DXT1"))), recoverMipRecords: true, ct: new CancellationToken(true)));
    }
    [Fact] public void ExportsRecoveredBlocksForIndependentDecoder()
    {
        byte[] single = ImagingTests.SolidDds(71);
        // Convert the DX10 BC1 fixture to its equivalent legacy header before adding records.
        byte[] legacy = new byte[single.Length - 20]; single[..128].CopyTo(legacy, 0); "DXT1"u8.CopyTo(legacy.AsSpan(84)); single[148..].CopyTo(legacy, 128);
        using var f = new Fixture(); f.File("source/a.dds", legacy);
        var c = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(c, new() { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        using var full = new MemoryStream(); new CpuMipProcessor().Process(new MemoryStream(legacy), full, plan.Items[0], legacy.Length, default);
        byte[] original = Fixture.MipRecords(full.ToArray()); var info = Dds.Inspect(new MemoryStream(original), recoverMipRecords: true).Info;
        using var output = new MemoryStream(); Dds.Reduce(new ForwardOnly(original), output, 0, original.Length, expected: info);
        if (Environment.GetEnvironmentVariable("PENGUTEX_ORACLE_FIXTURES") is { } folder)
        { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "records-71.dds"), output.ToArray()); }
    }
    private sealed class ForwardOnly(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); } }
}

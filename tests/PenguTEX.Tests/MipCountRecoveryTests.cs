using System.Buffers.Binary;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class MipCountRecoveryTests
{
    [Theory] [InlineData(71, 8)] [InlineData(77, 32)] [InlineData(98, 8)]
    public void ExactFullPayloadCanRecoverItsHeaderWithoutRecompressing(int dxgi, int maximum)
    {
        using var f = new Fixture(); byte[] data = Fixture.Dds(dxgi: dxgi);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 2);
        f.File("source/a.dds", data); var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var strict = Planner.Analyze(catalog, new() { Maximum = maximum }); Assert.Equal(Decision.Invalid, strict.Items[0].Decision);
        var plan = Planner.Analyze(catalog, new() { Maximum = maximum, RecoverMipCounts = true });
        var item = plan.Items[0]; Assert.Equal(2, item.Input!.DeclaredMipCount); Assert.Equal(5, item.Input.MipCount);
        Assert.Contains("Recovered source mip count", item.Reason);
        string output = f.Dir("output"), cache = f.Dir("cache");
        var built = JobRunner.Build(catalog, plan, output, cache);
        var file = Assert.Single(built.Manifest.Files); Assert.Equal(2, file.DeclaredSourceMipCount);
        using var original = new MemoryStream(data); Assert.Equal(JobRunner.Hash(original), file.SourceHash);
        Assert.Equal(maximum == 32 ? "RepairHeader" : "MipCopy", file.Operation);
        byte[] result = File.ReadAllBytes(Path.Combine(output, "textures/a.dds"));
        var info = Dds.Inspect(new MemoryStream(result)).Info; Assert.Null(info.DeclaredMipCount);
        Assert.Equal(data[(item.Input.HeaderBytes + (int)item.Input.RemovedBytes(item.DropMips))..], result[info.HeaderBytes..]);
        Assert.Equal(data, File.ReadAllBytes(catalog.Assets[0].Container));
        Assert.Equal(item.TargetWidth, TexturePreview.Decode(new MemoryStream(data), item.DropMips, data.Length, recoverMipCount: true).Width);
        var reused = JobRunner.Build(catalog, plan, output, cache); Assert.Equal(1, reused.Reused);
        Assert.Equal(2, reused.Manifest.Files[0].DeclaredSourceMipCount); Assert.True(reused.Manifest.OriginalsOnly);
    }
    [Fact] public void PartialChainsTrailersRawLayoutsAndProtectionsRemainStrict()
    {
        using var f = new Fixture(); var full = Fixture.Dds(); BinaryPrimitives.WriteUInt32LittleEndian(full.AsSpan(28), 2);
        var partial = Fixture.Dds(mipCount: 3); BinaryPrimitives.WriteUInt32LittleEndian(partial.AsSpan(28), 2);
        var raw = Fixture.RawDds(); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(28), 1);
        foreach (var bad in new[] { full[..^1], full.Concat(new byte[1]).ToArray(), partial, raw })
            Assert.Throws<InvalidDataException>(() => Dds.Inspect(new MemoryStream(bad), recoverMipCount: true));
        f.File("source/terrain/a.dds", full);
        var plan = Planner.Analyze(CatalogLoader.Load(new(Folder: f.Dir("source"))), new() { RecoverMipCounts = true });
        Assert.Equal(Decision.Invalid, plan.Items[0].Decision); Assert.Equal(0, plan.ChangedCount);
    }
    [Fact] public void UnsupportedRebuildCannotPassComparisonByOnlyRepairingItsInspectedMetadata()
    {
        using var f = new Fixture(); var bytes = Fixture.Dds(dxgi: 95); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 2);
        f.File("source/a.dds", bytes); f.File("reference/a.dds", Fixture.Dds(dxgi: 95));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")) { ReferenceFolder = f.Dir("reference") });
        var plan = Planner.Analyze(catalog, new() { RecoverMipCounts = true,
            Rules = [new("*", RebuildMips: true, Semantic: TextureSemantic.LinearData)] });
        Assert.Equal(Decision.Unsupported, plan.Items[0].Decision);
        Assert.True(plan.Comparison!.HasUnresolved); Assert.Equal(1, plan.Comparison.UnknownCount);
    }
}

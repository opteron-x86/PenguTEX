using System.Text;
using PenguTEX.Assets;
using PenguTEX.Assets.Materials;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class MaterialTests
{
    private static Policy Fresh => Policy.Preset("balanced") with { Maximum = 8, NormalMaximum = 8, MaterialMaximum = 8 };
    [Theory] [InlineData(0, 0, MaterialRole.Color, TextureSemantic.SrgbColor)]
    [InlineData(1, 0, MaterialRole.Normal, TextureSemantic.NormalRgb)]
    [InlineData(3, 3, MaterialRole.Height, TextureSemantic.LinearData)]
    [InlineData(5, 1, MaterialRole.Data, TextureSemantic.LinearData)]
    public void UsesShaderSlotsToCompleteOnlyMissingLevels(int slot, uint type, MaterialRole role, TextureSemantic semantic)
    {
        using var f = new Fixture(); byte[] original = Fixture.RawDds(mipCount: 2);
        f.File("source/textures/a.dds", original); f.File("source/meshes/shape.nif", Fixture.Mesh(slot, type: type));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")), inspectMaterials: true);
        var plan = Planner.Analyze(catalog, Fresh); var item = Assert.Single(plan.Items);
        Assert.Equal(Decision.Rebuild, item.Decision); Assert.True(item.PreserveExistingMips);
        Assert.Equal(role, item.Material!.Role); Assert.Equal(semantic, item.Semantic); Assert.Equal(1, item.DropMips);
        Assert.True(plan.Coverage.MeetsPolicy); Assert.Null(plan.Comparison);
        var build = JobRunner.Build(catalog, plan, f.Dir("output"), f.Dir("cache"), processor: new CpuMipProcessor());
        Assert.True(build.Manifest.OriginalsOnly); Assert.Equal(role, build.Manifest.Files[0].Material!.Role);
        byte[] bytes = File.ReadAllBytes(Path.Combine(build.Output, "textures/a.dds"));
        Assert.Equal(original[(128 + 16 * 8 * 4)..], bytes[128..(128 + 8 * 4 * 4)]);
        Assert.Equal(4, Dds.Inspect(new MemoryStream(bytes)).Info.MipCount);
    }
    [Theory] [InlineData(512 | (4 << 10), 90, MaterialRole.Cutout, 91)]
    [InlineData(512 | (6 << 10), 90, MaterialRole.Cutout, 90)]
    [InlineData(1 | (6 << 1) | (7 << 5), 128, MaterialRole.Blend, 128)]
    [InlineData(512 | (1 << 10), 128, MaterialRole.Unknown, 128)]
    [InlineData(1, 128, MaterialRole.Unknown, 128)]
    public void InterpretsAlphaComparisonsAndBlendFactors(ushort flags, byte threshold, MaterialRole role, int cutoff)
    {
        var use = Assert.Single(MaterialReader.ReadBindings(Fixture.Mesh(alpha: flags, threshold: threshold), "mesh", "source")).Use;
        Assert.Equal(role, use.Role); Assert.Equal(cutoff, use.AlphaCutoff);
    }
    [Fact] public void ConflictUnknownAndUnboundUsesStayVisibleWithoutFilenameGuesses()
    {
        using var f = new Fixture(); f.File("source/textures/a.dds", Fixture.RawDds(mipCount: 1));
        f.File("source/meshes/a.nif", Fixture.Mesh(0)); f.File("source/meshes/b.nif", Fixture.Mesh(1));
        var source = new SourceOptions(Folder: f.Dir("source"));
        var plan = Planner.Analyze(CatalogLoader.Load(source, inspectMaterials: true), Fresh);
        Assert.Equal("Conflicting bindings", plan.Items[0].Material!.Status); Assert.Equal(Decision.Keep, plan.Items[0].Decision);
        Assert.Equal(1, plan.Coverage.AboveLimit); Assert.Equal(1, plan.Coverage.IncompleteMips);
        f.File("source/meshes/b.nif", Fixture.Mesh(bound: false));
        plan = Planner.Analyze(CatalogLoader.Load(source, inspectMaterials: true), Fresh);
        Assert.Equal("Unsupported binding", plan.Items[0].Material!.Status);
        Directory.Delete(f.Dir("source/meshes"), true);
        plan = Planner.Analyze(CatalogLoader.Load(source, inspectMaterials: true), Fresh);
        Assert.Equal("Unbound", plan.Items[0].Material!.Status);
        var explicitPlan = Planner.Analyze(CatalogLoader.Load(source, inspectMaterials: true), Fresh with { Rules = [new("*", CompleteMipChain: true, Semantic: TextureSemantic.LinearData)] });
        Assert.Equal(Decision.Rebuild, explicitPlan.Items[0].Decision);
    }
    [Fact] public void NormalBindingsRejectSrgbAndReconstructBc5WithoutSwizzling()
    {
        using var f = new Fixture(); f.File("source/textures/a.dds", Fixture.Dds(dxgi: 99, mipCount: 1)); f.File("source/meshes/a.nif", Fixture.Mesh(1));
        var source = new SourceOptions(Folder: f.Dir("source"));
        Assert.Equal(Decision.Unsupported, Planner.Analyze(CatalogLoader.Load(source, inspectMaterials: true), Fresh).Items[0].Decision);
        f.File("source/textures/a.dds", Fixture.Dds(dxgi: 83, mipCount: 1));
        Assert.Equal(TextureSemantic.NormalRg, Planner.Analyze(CatalogLoader.Load(source, inspectMaterials: true), Fresh).Items[0].Semantic);
    }
    [Fact] public void ChangedMaterialEvidenceCannotPublishAndCancellationIsHonored()
    {
        using var f = new Fixture(); f.File("source/textures/a.dds", Fixture.RawDds(mipCount: 1)); f.File("source/meshes/a.nif", Fixture.Mesh());
        var source = new SourceOptions(Folder: f.Dir("source")); var catalog = CatalogLoader.Load(source, inspectMaterials: true); var plan = Planner.Analyze(catalog, Fresh);
        f.File("source/meshes/a.nif", Fixture.Mesh(1));
        Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, f.Dir("output"), f.Dir("cache"), processor: new CpuMipProcessor()));
        Assert.Empty(Directory.EnumerateFiles(f.Dir("output")));
        Assert.ThrowsAny<OperationCanceledException>(() => CatalogLoader.Load(source, ct: new CancellationToken(true), inspectMaterials: true));
    }
    [Fact] public void TruncatedAndOversizedMaterialMetadataIsReported()
    {
        using var f = new Fixture(); f.File("source/meshes/broken.nif", Fixture.Mesh()[..70]);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")), inspectMaterials: true);
        Assert.Equal(1, catalog.Materials!.UnreadableMeshes); Assert.Contains(catalog.Diagnostics, d => d.Message.Contains("Material inspection unavailable"));
        var data = Fixture.Mesh(); Assert.Throws<InvalidDataException>(() => NifFile.Read(data[..20]));
    }
    [Fact] public void FreshPresetsOptimizeOrdinaryCharactersAndLandscapesAndPreserveCustomPolicies()
    {
        var p = Policy.Preset("balanced"); Assert.Null(p.Classify("textures/actors/character/face.dds").Protection);
        Assert.Null(p.Classify("textures/landscape/ground.dds").Protection);
        Assert.NotNull(p.Classify("textures/terrain/world/test.dds").Protection);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<Policy>("{\"Maximum\":4096}", JsonFiles.Options)!;
        Assert.Equal(4096, legacy.Maximum); Assert.True(legacy.ProtectCharacters); Assert.False(legacy.CompleteKnownMaterials);
    }
    [Fact] public void MeshWinnersFollowEnabledPluginArchiveOrderAndLoosePrecedence()
    {
        using var f = new Fixture(); string game = f.Dir("game"); f.Dir("game/Data");
        f.Text("instance/ModOrganizer.ini", $"[General]\ngamePath={game}\nselected_profile=Test\n");
        f.Text("instance/profiles/Test/settings.ini", "[General]\nLocalSettings=true\n");
        f.Text("instance/profiles/Test/skyrim.ini", "[Archive]\nsResourceArchiveList=\nsResourceArchiveList2=\n");
        f.Text("instance/profiles/Test/modlist.txt", "+Previous\n+High\n-Off\n+Low\n");
        f.Text("instance/profiles/Test/plugins.txt", "*A.esp\n*B.esp\n"); f.Text("instance/profiles/Test/loadorder.txt", "A.esp\nB.esp\n");
        f.File("instance/mods/Low/A.bsa", Fixture.Bsa(Fixture.Mesh(0), "shape.nif", "meshes"));
        f.File("instance/mods/High/B.bsa", Fixture.Bsa(Fixture.Mesh(1), "shape.nif", "meshes"));
        f.File("instance/mods/Off/meshes/shape.nif", Fixture.Mesh(5, type: 1));
        f.File("instance/mods/Previous/meshes/shape.nif", Fixture.Mesh(3, type: 3)); f.Text("instance/mods/Previous/VRAMrOutput.tmp", "");
        f.File("instance/mods/Low/textures/a.dds", Fixture.RawDds(mipCount: 1));
        var source = new SourceOptions(Instance: f.Dir("instance"));
        MaterialHint Hint() => CatalogLoader.Load(source, inspectMaterials: true).Materials!.Textures["textures/a.dds"];
        Assert.Equal(MaterialRole.Normal, Hint().Role);
        f.Text("instance/profiles/Test/plugins.txt", "*A.esp\n"); Assert.Equal(MaterialRole.Color, Hint().Role);
        f.File("instance/mods/High/meshes/shape.nif", Fixture.Mesh(5, type: 1)); Assert.Equal(MaterialRole.Data, Hint().Role);
        var before = Planner.Analyze(CatalogLoader.Load(source, inspectMaterials: true), Fresh);
        Directory.Delete(f.Dir("instance/mods/Previous"), true); f.Text("instance/profiles/Test/modlist.txt", "+High\n-Off\n+Low\n");
        var after = Planner.Analyze(CatalogLoader.Load(source, inspectMaterials: true), Fresh);
        Assert.Equal(before.ChangedCount, after.ChangedCount); Assert.Equal(before.MaterialFingerprint, after.MaterialFingerprint); Assert.Null(after.Comparison);
    }
    [Theory] [InlineData(0, MaterialRole.Color)] [InlineData(1, MaterialRole.Normal)]
    [InlineData(2, MaterialRole.Color)] [InlineData(3, MaterialRole.Height)] [InlineData(5, MaterialRole.Data)]
    [InlineData(7, MaterialRole.Unknown)]
    public void TruePbrRecognizesDeclaredSlotsAndLeavesFeatureMapsExplicit(int slot, MaterialRole role)
    {
        var binding = Assert.Single(MaterialReader.ReadBindings(Fixture.Mesh(slot, flags2: 1u << 23), "mesh", "container")).Use;
        Assert.Equal(role, binding.Role); Assert.Contains("TruePBR", binding.Detail);
    }
    [Fact] public void Bc1EncodingPreservesThePlannedMaterialMaskAtNonDefaultCutoff()
    {
        byte[] pixels = new byte[8 * 8 * 4]; int[] counts = [1, 3, 2, 2];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        {
            int at = (y * 8 + x) * 4; pixels[at] = 64; pixels[at + 1] = 128; pixels[at + 2] = 192;
            pixels[at + 3] = (y % 2 * 2 + x % 2) < counts[x / 2] ? (byte)255 : (byte)0;
        }
        var encoder = new BCnEncoder.Encoder.BcEncoder(BCnEncoder.Shared.CompressionFormat.Bc1WithAlpha);
        encoder.OutputOptions.GenerateMipMaps = false; encoder.Options.IsParallel = false;
        byte[] original = Fixture.Dds(8, 8, fourCC: "DXT1", mipCount: 1);
        encoder.EncodeToRawBytes(pixels, 8, 8, BCnEncoder.Encoder.PixelFormat.Rgba32)[0].CopyTo(original, 128);
        using var f = new Fixture(); f.File("source/a.dds", original); var c = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(c, new() { Rules = [new("*", CompleteMipChain: true, Semantic: TextureSemantic.OpacitySrgb, AlphaCutoff: 192)] });
        using var output = new MemoryStream(); new CpuMipProcessor().Process(new MemoryStream(original), output, plan.Items[0], original.Length, default);
        var sourcePixels = TexturePreview.Decode(new MemoryStream(original));
        var expected = MipFilter.Downsample(sourcePixels, TextureSemantic.OpacitySrgb, MipFilter.Coverage(sourcePixels, 192), alphaCutoff: 192);
        output.Position = 0; var actual = TexturePreview.Decode(output, 1);
        for (int a = 3; a < expected.Rgba.Length; a += 4) Assert.Equal(expected.Rgba[a] >= 192 ? (byte)255 : (byte)0, actual.Rgba[a]);
        if (Environment.GetEnvironmentVariable("PENGUTEX_ORACLE_FIXTURES") is { } folder)
        { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, "cutout-bc1.dds"), output.ToArray()); }
    }
    [Fact] public void BlendFilteringPreservesMeanAlphaAndCutoutUsesItsActualThreshold()
    {
        var pixels = new PixelImage([255,0,0,255, 0,0,255,0, 255,0,0,255, 0,0,255,0], 2, 2);
        var blend = MipFilter.Downsample(pixels, TextureSemantic.OpacityBlendSrgb);
        Assert.Equal(new byte[] {255,0,0,128}, blend.Rgba);
        var cutout = MipFilter.Downsample(pixels, TextureSemantic.OpacitySrgb, 1, alphaCutoff: 192);
        Assert.True(cutout.Rgba[3] >= 192); Assert.Equal(1, MipFilter.Coverage(cutout, 192));
    }
}

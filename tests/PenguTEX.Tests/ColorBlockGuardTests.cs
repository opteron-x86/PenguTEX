using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Tests;

public sealed class ColorBlockGuardTests
{
    // A real generated normal-map block and the colour block BCnEncoder 2.3.0 produced for it: every
    // input has blue >= 245, but the second endpoint's blue wrapped to 24.
    private static readonly int[] Texels =
    [
        131, 129, 255, 131, 129, 255, 131, 129, 255, 131, 129, 255, 131, 129, 255, 131, 129, 255, 131, 129, 255, 131, 129, 255,
        132, 125, 255, 169, 155, 245, 123, 166, 249, 123, 166, 249, 132, 125, 255, 152, 141, 252, 123, 121, 255, 123, 121, 255,
    ];
    private static byte[] Overflowed => Convert.FromHexString("1F95837BFFFF03FB");
    private static byte[] Rgba(byte alpha = 255) => Enumerable.Range(0, 16).SelectMany(i => new[] { (byte)Texels[i * 3], (byte)Texels[i * 3 + 1], (byte)Texels[i * 3 + 2], alpha }).ToArray();
    private static ColorRgba32[] Decode(byte[] blocks, int width, int rows, CompressionFormat format = CompressionFormat.Bc1WithAlpha)
    { var decoder = new BcDecoder(); decoder.Options.IsParallel = false; return decoder.DecodeRaw(blocks, width, rows, format); }

    [Fact] public void OverflowedEndpointIsReplacedByABoundedFit()
    {
        byte[] block = Overflowed; Assert.Contains(Decode(block, 4, 4), p => p.b < 128);
        Assert.Equal(1, ColorBlockGuard.Repair(Rgba(), 4, 4, block, 8));
        var repaired = Decode(block, 4, 4); byte[] input = Rgba();
        for (int i = 0; i < 16; i++)
        {
            Assert.InRange(repaired[i].b, 240, 255); Assert.Equal(255, repaired[i].a);
            Assert.InRange(Math.Abs(repaired[i].r - input[i * 4]), 0, 40); Assert.InRange(Math.Abs(repaired[i].g - input[i * 4 + 1]), 0, 40); // four colours on one line
        }
        byte[] again = (byte[])block.Clone(); Assert.Equal(0, ColorBlockGuard.Repair(Rgba(), 4, 4, again, 8)); Assert.Equal(block, again);
    }

    [Fact] public void WellEncodedBlocksStayByteIdentical()
    {
        byte[] rgba = new byte[16 * 4 * 4];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 16; x++) { int at = (y * 16 + x) * 4; rgba[at] = (byte)(40 + x * 9); rgba[at + 1] = (byte)(90 + y * 12); rgba[at + 2] = (byte)(120 - x * 3); rgba[at + 3] = 255; }
        foreach (var (format, bytes) in new[] { (CompressionFormat.Bc1WithAlpha, 8), (CompressionFormat.Bc3, 16) })
        {
            var encoder = new BcEncoder(format); encoder.OutputOptions.GenerateMipMaps = false; encoder.Options.IsParallel = false;
            byte[] blocks = encoder.EncodeToRawBytes(rgba, 16, 4, PixelFormat.Rgba32)[0], original = (byte[])blocks.Clone();
            Assert.Equal(0, ColorBlockGuard.Repair(rgba, 16, 4, blocks, bytes)); Assert.Equal(original, blocks);
        }
    }

    [Fact] public void TransparentBc1BlocksAndBc3AlphaAreLeftAlone()
    {
        byte[] block = Overflowed, cutout = Rgba(); cutout[3] = 0;
        Assert.Equal(0, ColorBlockGuard.Repair(cutout, 4, 4, block, 8)); Assert.Equal(Overflowed, block);
        // BC2/BC3 colour is the last eight bytes and is always four-colour; its hidden texels still have colour.
        byte[] bc3 = [0xFF, 0x00, 0x49, 0x92, 0x24, 0x49, 0x92, 0x24, ..Overflowed];
        Assert.Equal(1, ColorBlockGuard.Repair(cutout, 4, 4, bc3, 16));
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x49, 0x92, 0x24, 0x49, 0x92, 0x24 }, bc3[..8]); Assert.NotEqual(Overflowed, bc3[8..]);
        Assert.All(Decode(bc3, 4, 4, CompressionFormat.Bc3), p => Assert.InRange(p.b, 240, 255));
    }

    [Fact] public void PartialBlocksAndUniformColoursAreHandled()
    {
        // Six texels wide and two rows high: one full-width block and one two-texel block, both a single colour.
        byte[] rgba = Enumerable.Range(0, 12).SelectMany(_ => new byte[] { 131, 129, 255, 255 }).ToArray();
        byte[] blocks = [..Overflowed, ..Overflowed];
        Assert.Equal(2, ColorBlockGuard.Repair(rgba, 6, 2, blocks, 8));
        Assert.All(Decode(blocks, 6, 2), p => { Assert.InRange(p.r, 127, 135); Assert.InRange(p.g, 125, 133); Assert.Equal(255, p.b); Assert.Equal(255, p.a); });
        Assert.Throws<ArgumentException>(() => ColorBlockGuard.Repair(rgba, 6, 2, new byte[8], 8));
        Assert.Throws<ArgumentException>(() => ColorBlockGuard.Repair(rgba, 6, 5, blocks, 8));
        Assert.Throws<ArgumentException>(() => ColorBlockGuard.Repair(rgba, 6, 2, new byte[24], 12));
    }

    [Fact] public void CompletedNormalMapLevelsKeepTheirSaturatedChannel()
    {
        // An 8×8 single-level BC1 normal map whose first generated level is the block above.
        using var f = new Fixture(); byte[] source = Fixture.Dds(8, 8, fourCC: "DXT1", mipCount: 1);
        Convert.FromHexString("1F841F84000000001F841F8400000000DEA4FF830505A5A53E7DDF7B00005555").CopyTo(source, 128);
        f.File("source/textures/normal.dds", source);
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var policy = new Policy { Rules = [new("*", CompleteMipChain: true, Semantic: TextureSemantic.NormalRgb)] };
        var plan = Planner.Analyze(catalog, policy); Assert.Equal(Decision.Rebuild, Assert.Single(plan.Items).Decision);
        var build = JobRunner.Build(catalog, plan, Path.Combine(f.Root, "output"), Path.Combine(f.Root, "cache"), processor: new CpuMipProcessor());
        Assert.Contains("colourguard", Assert.Single(build.Manifest.Files).Processor);
        byte[] output = File.ReadAllBytes(Path.Combine(build.Output, "textures/normal.dds"));
        Assert.Equal(source[128..], output[128..(128 + 32)]);
        var level = TexturePreview.Decode(new MemoryStream(output), 1); Assert.Equal(4, level.Width);
        for (int i = 0; i < 16; i++) Assert.InRange(level.Rgba[i * 4 + 2], 240, 255);
    }
}

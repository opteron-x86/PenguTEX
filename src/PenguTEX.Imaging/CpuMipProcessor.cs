using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using PenguTEX.Core;

namespace PenguTEX.Imaging;

public sealed class CpuMipProcessor : ITextureProcessor
{
    public string Identity => "bcn-2.3.0-strips-raw-area-v2";
    // One decoded texture per process; other workers can continue streaming mip copies.
    private static readonly SemaphoreSlim MemorySlot = new(1, 1);
    public void Process(Stream source, Stream target, PlanItem item, long length, CancellationToken ct)
    {
        MemorySlot.Wait(ct);
        try
        {
            var (info, header) = Dds.Inspect(source, length);
            if (info != item.Input) throw new IOException("Source metadata changed; analyze again.");
            if (item.Semantic is not { } semantic) throw new InvalidDataException("Rebuilding requires explicit channel semantics.");
            if (Policy.RebuildRestriction(info, semantic) is { } restriction) throw new NotSupportedException(restriction);
            var pixels = TexturePreview.DecodePayload(source, info, ct: ct, maximumPixels: Policy.MaximumRebuildPixels);
            double coverage = semantic == TextureSemantic.OpacitySrgb ? MipFilter.Coverage(pixels) : 0;
            for (int n = 0; n < item.DropMips; n++) pixels = MipFilter.Downsample(pixels, semantic, coverage, ct);
            if (pixels.Width != item.TargetWidth || pixels.Height != item.TargetHeight) throw new InvalidDataException("Rebuild dimensions do not match plan.");
            int mips = 1 + (int)Math.Log2(Math.Max(pixels.Width, pixels.Height));
            Dds.ResizeHeader(header, info, pixels.Width, pixels.Height, mips); target.Write(header);
            var format = info.IsBlockCompressed ? TexturePreview.Format(info) : CompressionFormat.Unknown;
            var encoder = new BcEncoder(format);
            encoder.OutputOptions.GenerateMipMaps = false; encoder.OutputOptions.Quality = CompressionQuality.Balanced;
            encoder.Options.IsParallel = false;
            var decoder = new BcDecoder(); decoder.Options.IsParallel = false;
            for (int mip = 0; mip < mips; mip++)
            {
                ct.ThrowIfCancellationRequested();
                if (!info.IsBlockCompressed) RawPixels.Write(target, pixels, info, ct);
                else for (int y = 0; y < pixels.Height; y += 4)
                {
                    ct.ThrowIfCancellationRequested(); int rows = Math.Min(4, pixels.Height - y);
                    var strip = pixels.Rgba.AsSpan(y * pixels.Width * 4, pixels.Width * rows * 4);
                    var blocks = encoder.EncodeToRawBytes(strip, pixels.Width, rows, PixelFormat.Rgba32)[0];
                    long expected = (long)((pixels.Width + 3) / 4) * info.BlockBytes;
                    if (blocks.Length != expected) throw new InvalidDataException("Encoder returned an unexpected block count.");
                    if (decoder.DecodeRaw(blocks, pixels.Width, rows, format).Length != pixels.Width * rows)
                        throw new InvalidDataException("Generated strip did not decode to its expected dimensions.");
                    target.Write(blocks);
                }
                if (mip + 1 < mips) pixels = MipFilter.Downsample(pixels, semantic, coverage, ct);
            }
        }
        finally { MemorySlot.Release(); }
    }
}

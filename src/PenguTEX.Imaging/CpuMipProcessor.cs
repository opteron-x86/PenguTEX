using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using PenguTEX.Core;

namespace PenguTEX.Imaging;

public sealed class CpuMipProcessor : ITextureProcessor
{
    public string Identity => "bcn-2.3.0-balanced-area-v1";
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
            var pixels = TexturePreview.DecodePayload(source, info, ct: ct);
            double coverage = semantic == TextureSemantic.OpacitySrgb ? MipFilter.Coverage(pixels) : 0;
            for (int n = 0; n < item.DropMips; n++) pixels = MipFilter.Downsample(pixels, semantic, coverage, ct);
            if (pixels.Width != item.TargetWidth || pixels.Height != item.TargetHeight) throw new InvalidDataException("Rebuild dimensions do not match plan.");
            int mips = 1 + (int)Math.Log2(Math.Max(pixels.Width, pixels.Height));
            Dds.ResizeHeader(header, info, pixels.Width, pixels.Height, mips); target.Write(header);
            var format = TexturePreview.Format(info); var encoder = new BcEncoder(format);
            encoder.OutputOptions.GenerateMipMaps = false; encoder.OutputOptions.Quality = CompressionQuality.Balanced;
            encoder.Options.IsParallel = false;
            var decoder = new BcDecoder(); decoder.Options.IsParallel = false;
            for (int mip = 0; mip < mips; mip++)
            {
                ct.ThrowIfCancellationRequested();
                var blocks = encoder.EncodeToRawBytesAsync(pixels.Rgba, pixels.Width, pixels.Height, PixelFormat.Rgba32, ct).GetAwaiter().GetResult()[0];
                long expected = (long)((pixels.Width + 3) / 4) * ((pixels.Height + 3) / 4) * info.BlockBytes;
                if (blocks.Length != expected) throw new InvalidDataException("Encoder returned an unexpected block count.");
                // Decode every generated level independently of the encoder and DDS header writer.
                using var check = new MemoryStream(blocks, false);
                if (decoder.DecodeRaw(check, pixels.Width, pixels.Height, format).Length != pixels.Width * pixels.Height)
                    throw new InvalidDataException("Generated mip did not decode to its expected dimensions.");
                target.Write(blocks);
                if (mip + 1 < mips) pixels = MipFilter.Downsample(pixels, semantic, coverage, ct);
            }
        }
        finally { MemorySlot.Release(); }
    }
}

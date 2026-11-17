using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using PenguTEX.Core;

namespace PenguTEX.Imaging;

public sealed class CpuMipProcessor : ITextureProcessor
{
    private readonly Bc7Encoder bc7;
    public CpuMipProcessor(Bc7Backend backend = Bc7Backend.Auto) { bc7 = new(backend); }
    public string EncoderSummary => bc7.Summary;
    public string Identity => "bcn-2.3.0-strips-raw-area-complete-alpha-bc1-colourguard-v6"
        + (bc7.Backend == Bc7Backend.Managed ? "" : "-" + bc7.Identity);
    // Decoded pixels across all concurrent rebuilds stay within one maximum-size texture. Small
    // textures run side by side; streaming mip copies are not counted.
    public static PixelBudget Budget { get; } = new(Policy.MaximumRebuildPixels);
    public void Process(Stream source, Stream target, PlanItem item, long length, CancellationToken ct)
    {
        var (info, header) = Dds.Inspect(source, length, item.Input?.DeclaredMipCount is not null);
        if (info != item.Input) throw new IOException("Source metadata changed; analyze again.");
        if (item.Semantic is not { } semantic) throw new InvalidDataException("Rebuilding requires explicit channel semantics.");
        if (Policy.RebuildRestriction(info, semantic, item.PreserveExistingMips) is { } restriction) throw new NotSupportedException(restriction);
        // Completion decodes only the smallest original level; a rebuild decodes the top one.
        int decoded = item.PreserveExistingMips ? info.MipCount - 1 : 0;
        using var reservation = Budget.Reserve((long)Math.Max(1, info.Width >> decoded) * Math.Max(1, info.Height >> decoded), ct);
        int mips = Dds.FullMipCount(item.TargetWidth, item.TargetHeight);
        Dds.ResizeHeader(header, info, item.TargetWidth, item.TargetHeight, mips); target.Write(header);
        PixelImage pixels; int firstGenerated = 0, downsample = item.DropMips;
        if (item.PreserveExistingMips)
        {
            int last = info.MipCount - 1;
            int start = Math.Min(item.DropMips, last);
            Dds.Transfer(source, Stream.Null, info.RemovedBytes(start), ct);
            Dds.Transfer(source, target, info.RemovedBytes(last) - info.RemovedBytes(start), ct);
            var smallest = info with { Width = Math.Max(1, info.Width >> last), Height = Math.Max(1, info.Height >> last), MipCount = 1 };
            // Decode the last original level while copying it, without buffering a second full image.
            Stream input = item.DropMips <= last ? new CopyingReadStream(source, target) : source;
            pixels = TexturePreview.DecodePayload(input, smallest, ct: ct, maximumPixels: Policy.MaximumRebuildPixels);
            if (item.DropMips <= last) firstGenerated = info.MipCount - item.DropMips;
            downsample = item.DropMips <= last ? 1 : item.DropMips - last;
            if (source.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing DDS payload.");
            if (firstGenerated == mips) return;
        }
        else pixels = TexturePreview.DecodePayload(source, info, ct: ct, maximumPixels: Policy.MaximumRebuildPixels);
        double coverage = semantic == TextureSemantic.OpacitySrgb ? MipFilter.Coverage(pixels, item.AlphaCutoff) : 0;
        for (int n = 0; n < downsample; n++) pixels = MipFilter.Downsample(pixels, semantic, coverage, ct, item.AlphaCutoff);
        if (pixels.Width != Math.Max(1, item.TargetWidth >> firstGenerated) || pixels.Height != Math.Max(1, item.TargetHeight >> firstGenerated))
            throw new InvalidDataException("Rebuild dimensions do not match plan.");
        var format = info.IsBlockCompressed ? TexturePreview.Format(info) : CompressionFormat.Unknown;
        // JobRunner shares the processor across workers; managed codec instances are per texture.
        var bc7Encoder = info.Format == "BC7" ? new Bc7Encoder(bc7.Backend) : null;
        var encoder = new BcEncoder(format);
        encoder.OutputOptions.GenerateMipMaps = false; encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.Options.IsParallel = false;
        var decoder = new BcDecoder(); decoder.Options.IsParallel = false;
        for (int mip = firstGenerated; mip < mips; mip++)
        {
            ct.ThrowIfCancellationRequested();
            if (!info.IsBlockCompressed) RawPixels.Write(target, pixels, info, ct);
            else for (int y = 0; y < pixels.Height; y += 4)
            {
                ct.ThrowIfCancellationRequested(); int rows = Math.Min(4, pixels.Height - y);
                var strip = pixels.Rgba.AsSpan(y * pixels.Width * 4, pixels.Width * rows * 4);
                if (info.Format == "BC1" && semantic == TextureSemantic.OpacitySrgb)
                {
                    // BC1's encoder uses a fixed binary-alpha threshold. Quantize the planned
                    // material mask in a strip copy; keep continuous alpha for the next filter.
                    byte[] mask = strip.ToArray();
                    for (int a = 3; a < mask.Length; a += 4) mask[a] = mask[a] >= item.AlphaCutoff ? (byte)255 : (byte)0;
                    strip = mask;
                }
                var blocks = bc7Encoder is not null ? bc7Encoder.EncodeStrip(strip, pixels.Width, rows)
                    : encoder.EncodeToRawBytes(strip, pixels.Width, rows, PixelFormat.Rgba32)[0];
                long expected = (long)((pixels.Width + 3) / 4) * info.BlockBytes;
                if (blocks.Length != expected) throw new InvalidDataException("Encoder returned an unexpected block count.");
                // The library's BC1 colour encoder can overflow an endpoint on saturated channels.
                if (info.Format is "BC1" or "BC2" or "BC3") ColorBlockGuard.Repair(strip, pixels.Width, rows, blocks, info.BlockBytes);
                if (decoder.DecodeRaw(blocks, pixels.Width, rows, format).Length != pixels.Width * rows)
                    throw new InvalidDataException("Generated strip did not decode to its expected dimensions.");
                target.Write(blocks);
            }
            if (mip + 1 < mips) pixels = MipFilter.Downsample(pixels, semantic, coverage, ct, item.AlphaCutoff);
        }
    }

    private sealed class CopyingReadStream(Stream source, Stream target) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int read = source.Read(buffer); target.Write(buffer[..read]); return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

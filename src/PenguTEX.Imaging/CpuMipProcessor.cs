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
        // The layout written: the source's own, or BC7/BC4 when an uncompressed source is compressed.
        var output = item.OutputInfo!;
        bool convert = item.OutputFormat is not null;
        if (convert && (info.IsBlockCompressed || Policy.CompressionTarget(info) != item.OutputFormat))
            throw new NotSupportedException("Only uncompressed 8-bit sources are compressed, to the planned BC7 or BC4 format.");
        var semantic = item.Semantic;
        int last = info.MipCount - 1;
        if (semantic is null)
        {
            // Encoding alone keeps the source's levels from the dropped one down; nothing is filtered.
            if (!convert || !item.PreserveExistingMips || item.DropMips > last) throw new InvalidDataException("Rebuilding requires explicit channel semantics.");
        }
        else if (Policy.RebuildRestriction(info, semantic.Value, item.PreserveExistingMips) is { } restriction) throw new NotSupportedException(restriction);
        // Copied levels are never decoded. Encoded levels are decoded one at a time, largest first;
        // completion decodes the smallest original level and a rebuild the top one.
        int decoded = !item.PreserveExistingMips ? 0 : convert ? Math.Min(item.DropMips, last) : last;
        using var reservation = Budget.Reserve((long)Math.Max(1, info.Width >> decoded) * Math.Max(1, info.Height >> decoded), ct);
        int mips = semantic is null ? info.MipCount - item.DropMips : Dds.FullMipCount(item.TargetWidth, item.TargetHeight);
        if (convert) header = Dds.CreateHeader(output, item.TargetWidth, item.TargetHeight, mips);
        else Dds.ResizeHeader(header, info, item.TargetWidth, item.TargetHeight, mips);
        target.Write(header);
        var format = output.IsBlockCompressed ? TexturePreview.Format(output) : CompressionFormat.Unknown;
        // JobRunner shares the processor across workers; managed codec instances are per texture.
        var bc7Encoder = output.Format == "BC7" ? new Bc7Encoder(bc7.Backend) : null;
        var encoder = new BcEncoder(format);
        encoder.OutputOptions.GenerateMipMaps = false; encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.Options.IsParallel = false;
        var decoder = new BcDecoder(); decoder.Options.IsParallel = false;
        // Alpha-tested textures must keep the share of visible texels when their levels are newly compressed.
        int? alphaTest = convert && (semantic == TextureSemantic.OpacitySrgb || item.Material?.Role == MaterialRole.Cutout) ? item.AlphaCutoff : null;
        PixelImage pixels; int firstGenerated = 0, downsample = item.DropMips;
        if (item.PreserveExistingMips)
        {
            int start = Math.Min(item.DropMips, last);
            Dds.Transfer(source, Stream.Null, info.RemovedBytes(start), ct);
            if (convert)
            {
                // Each kept level is read and encoded as it is; the last one also seeds any missing levels.
                pixels = null!;
                for (int level = start; level <= last; level++)
                {
                    ct.ThrowIfCancellationRequested();
                    pixels = RawPixels.Read(source, info, level, ct);
                    if (item.DropMips <= last) EncodeLevel(pixels);
                }
            }
            else
            {
                Dds.Transfer(source, target, info.RemovedBytes(last) - info.RemovedBytes(start), ct);
                var smallest = info with { Width = Math.Max(1, info.Width >> last), Height = Math.Max(1, info.Height >> last), MipCount = 1 };
                // Decode the last original level while copying it, without buffering a second full image.
                Stream input = item.DropMips <= last ? new CopyingReadStream(source, target) : source;
                pixels = TexturePreview.DecodePayload(input, smallest, ct: ct, maximumPixels: Policy.MaximumRebuildPixels);
            }
            if (item.DropMips <= last) firstGenerated = info.MipCount - item.DropMips;
            downsample = item.DropMips <= last ? 1 : item.DropMips - last;
            if (source.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing DDS payload.");
            if (firstGenerated == mips) return;
        }
        else pixels = TexturePreview.DecodePayload(source, info, ct: ct, maximumPixels: Policy.MaximumRebuildPixels);
        var filter = semantic!.Value;
        double coverage = filter == TextureSemantic.OpacitySrgb ? MipFilter.Coverage(pixels, item.AlphaCutoff) : 0;
        for (int n = 0; n < downsample; n++) pixels = MipFilter.Downsample(pixels, filter, coverage, ct, item.AlphaCutoff);
        if (pixels.Width != Math.Max(1, item.TargetWidth >> firstGenerated) || pixels.Height != Math.Max(1, item.TargetHeight >> firstGenerated))
            throw new InvalidDataException("Rebuild dimensions do not match plan.");
        for (int mip = firstGenerated; mip < mips; mip++)
        {
            ct.ThrowIfCancellationRequested();
            EncodeLevel(pixels);
            if (mip + 1 < mips) pixels = MipFilter.Downsample(pixels, filter, coverage, ct, item.AlphaCutoff);
        }

        void EncodeLevel(PixelImage level)
        {
            if (!output.IsBlockCompressed) { RawPixels.Write(target, level, output, ct); return; }
            long visibleBefore = 0, visibleAfter = 0;
            for (int y = 0; y < level.Height; y += 4)
            {
                ct.ThrowIfCancellationRequested(); int rows = Math.Min(4, level.Height - y);
                var strip = level.Rgba.AsSpan(y * level.Width * 4, level.Width * rows * 4);
                var original = strip;
                if ((output.Format == "BC1" && semantic == TextureSemantic.OpacitySrgb) || alphaTest is not null)
                {
                    // An alpha-tested material only sees which side of its cutoff each texel is on. BC1's encoder uses a
                    // fixed binary threshold, and a lossy BC7 alpha thins fine cutouts in small levels, so quantize the
                    // mask at the material's cutoff in a strip copy; continuous alpha stays for the next filter.
                    byte[] mask = strip.ToArray();
                    for (int a = 3; a < mask.Length; a += 4) mask[a] = mask[a] >= item.AlphaCutoff ? (byte)255 : (byte)0;
                    strip = mask;
                }
                var blocks = bc7Encoder is not null ? bc7Encoder.EncodeStrip(strip, level.Width, rows)
                    : encoder.EncodeToRawBytes(strip, level.Width, rows, PixelFormat.Rgba32)[0];
                long expected = (long)((level.Width + 3) / 4) * output.BlockBytes;
                if (blocks.Length != expected) throw new InvalidDataException("Encoder returned an unexpected block count.");
                // The library's BC1 colour encoder can overflow an endpoint on saturated channels.
                if (output.Format is "BC1" or "BC2" or "BC3") ColorBlockGuard.Repair(strip, level.Width, rows, blocks, output.BlockBytes);
                var check = decoder.DecodeRaw(blocks, level.Width, rows, format);
                if (check.Length != level.Width * rows) throw new InvalidDataException("Generated strip did not decode to its expected dimensions.");
                if (alphaTest is { } cutoff)
                    for (int i = 0; i < check.Length; i++)
                    {
                        if (original[i * 4 + 3] >= cutoff) visibleBefore++;
                        if (check[i].a >= cutoff) visibleAfter++;
                    }
                target.Write(blocks);
            }
            // With the mask quantized, any visible-texel change beyond two percentage points means an encoder defect.
            if (alphaTest is not null && Math.Abs(visibleAfter - visibleBefore) > 0.02 * level.Width * level.Height && level.Width * level.Height >= 256)
                throw new InvalidDataException($"Compressing {item.Path} to {output.Format} changed its alpha-test coverage from {visibleBefore} to {visibleAfter} texels.");
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

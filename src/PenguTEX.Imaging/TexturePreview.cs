using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using PenguTEX.Core;

namespace PenguTEX.Imaging;

public sealed record PixelImage(byte[] Rgba, int Width, int Height);
public static class TexturePreview
{
    public const long MaximumPreviewPixels = 4096L * 4096;
    public static CompressionFormat Format(DdsInfo info)
    {
        if (info.Signed) throw new NotSupportedException("Signed BC4/BC5 are retained by mip copying; this decoder does not preview signed channels.");
        return info.Format switch
        {
            "BC1" => CompressionFormat.Bc1WithAlpha, "BC2" => CompressionFormat.Bc2, "BC3" => CompressionFormat.Bc3,
            "BC4" => CompressionFormat.Bc4, "BC5" => CompressionFormat.Bc5, "BC7" => CompressionFormat.Bc7,
            _ => throw new NotSupportedException("Unsupported preview format."),
        };
    }
    public static PixelImage Decode(Stream source, int mip = 0, long? length = null, CancellationToken ct = default, bool recoverMipCount = false)
    {
        var (info, _) = Dds.Inspect(source, length, recoverMipCount);
        return DecodePayload(source, info, mip, ct);
    }
    public static PixelImage DecodePayload(Stream source, DdsInfo info, int mip = 0, CancellationToken ct = default,
        long maximumPixels = MaximumPreviewPixels)
    {
        if (mip < 0 || mip >= info.MipCount) throw new ArgumentOutOfRangeException(nameof(mip));
        int width = Math.Max(1, info.Width >> mip), height = Math.Max(1, info.Height >> mip);
        if ((long)width * height > maximumPixels) throw new NotSupportedException($"Decode exceeds the {maximumPixels:N0} pixel limit; select a smaller mip level.");
        Dds.Transfer(source, Stream.Null, info.RemovedBytes(mip), ct); ct.ThrowIfCancellationRequested();
        if (!info.IsBlockCompressed) return RawPixels.Read(source, info, mip, ct);
        var decoder = new BcDecoder(); decoder.Options.IsParallel = false;
        var format = Format(info);
        byte[] rgba = new byte[checked(width * height * 4)];
        byte[] blocks = new byte[checked((int)info.RowBytes(mip))];
        for (int y = 0; y < height; y += 4)
        {
            ct.ThrowIfCancellationRequested(); source.ReadExactly(blocks);
            var pixels = decoder.DecodeRaw(blocks, width, Math.Min(4, height - y), format);
            for (int i = 0; i < pixels.Length; i++)
            {
                int at = (y * width + i) * 4; rgba[at] = pixels[i].r; rgba[at + 1] = pixels[i].g;
                rgba[at + 2] = pixels[i].b; rgba[at + 3] = pixels[i].a;
            }
        }
        ct.ThrowIfCancellationRequested(); return new(rgba, width, height);
    }
    public static PixelImage Channel(PixelImage source, string channel)
    {
        byte[] rgba = (byte[])source.Rgba.Clone();
        int index = channel switch { "Red" => 0, "Green" => 1, "Blue" => 2, "Alpha / data" => 3, _ => -1 };
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (index >= 0) rgba[i] = rgba[i + 1] = rgba[i + 2] = source.Rgba[i + index];
            rgba[i + 3] = 255;
        }
        return new(rgba, source.Width, source.Height);
    }
}

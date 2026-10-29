using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using PenguTEX.Core;

namespace PenguTEX.Imaging;

public sealed record PixelImage(byte[] Rgba, int Width, int Height);
public static class TexturePreview
{
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
    public static PixelImage Decode(Stream source, int mip = 0, long? length = null, CancellationToken ct = default)
    {
        var (info, _) = Dds.Inspect(source, length);
        if (mip < 0 || mip >= info.MipCount) throw new ArgumentOutOfRangeException(nameof(mip));
        int width = Math.Max(1, info.Width >> mip), height = Math.Max(1, info.Height >> mip);
        if ((long)width * height > 4096L * 4096) throw new NotSupportedException("Preview is limited to 16 million pixels; select a smaller mip level.");
        Dds.Transfer(source, Stream.Null, info.RemovedBytes(mip), ct); ct.ThrowIfCancellationRequested();
        var decoder = new BcDecoder(); decoder.Options.IsParallel = false;
        var pixels = decoder.DecodeRaw(source, width, height, Format(info));
        byte[] rgba = new byte[checked(width * height * 4)];
        for (int i = 0; i < pixels.Length; i++) { rgba[i * 4] = pixels[i].r; rgba[i * 4 + 1] = pixels[i].g; rgba[i * 4 + 2] = pixels[i].b; rgba[i * 4 + 3] = pixels[i].a; }
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

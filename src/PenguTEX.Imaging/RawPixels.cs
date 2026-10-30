using System.Numerics;
using PenguTEX.Core;

namespace PenguTEX.Imaging;

// Byte-channel DDS layouts only. Higher precision, float and signed data never enter this path.
internal static class RawPixels
{
    private static byte Unpack(uint pixel, uint mask, byte missing = 0)
    {
        if (mask == 0) return missing;
        int shift = BitOperations.TrailingZeroCount(mask); uint maximum = mask >> shift;
        return (byte)((((pixel & mask) >> shift) * 255 + maximum / 2) / maximum);
    }
    private static uint Pack(byte value, uint mask)
    {
        if (mask == 0) return 0;
        int shift = BitOperations.TrailingZeroCount(mask); uint maximum = mask >> shift;
        return ((value * maximum + 127) / 255) << shift;
    }
    public static PixelImage Read(Stream source, DdsInfo info, int mip, CancellationToken ct)
    {
        if (!info.HasByteChannels) throw new NotSupportedException("This raw layout requires a precision-preserving decoder.");
        int width = Math.Max(1, info.Width >> mip), height = Math.Max(1, info.Height >> mip), stride = info.BitsPerPixel / 8;
        byte[] rgba = new byte[checked(width * height * 4)], row = new byte[checked((int)info.RowBytes(mip))];
        for (int y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested(); source.ReadExactly(row);
            for (int x = 0; x < width; x++)
            {
                uint pixel = 0; for (int b = 0; b < stride; b++) pixel |= (uint)row[x * stride + b] << (b * 8);
                int at = (y * width + x) * 4; rgba[at] = Unpack(pixel, info.RedMask);
                rgba[at + 1] = info.Luminance ? rgba[at] : Unpack(pixel, info.GreenMask);
                rgba[at + 2] = info.Luminance ? rgba[at] : Unpack(pixel, info.BlueMask);
                rgba[at + 3] = Unpack(pixel, info.AlphaMask, 255);
            }
        }
        return new(rgba, width, height);
    }
    public static void Write(Stream target, PixelImage image, DdsInfo info, CancellationToken ct)
    {
        if (!info.HasByteChannels) throw new NotSupportedException("This raw layout requires a precision-preserving encoder.");
        int stride = info.BitsPerPixel / 8;
        byte[] row = new byte[checked((int)(info with { Width = image.Width }).RowBytes(0))];
        for (int y = 0; y < image.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < image.Width; x++)
            {
                int at = (y * image.Width + x) * 4;
                uint pixel = Pack(image.Rgba[at], info.RedMask) | Pack(image.Rgba[at + 1], info.GreenMask)
                    | Pack(image.Rgba[at + 2], info.BlueMask) | Pack(image.Rgba[at + 3], info.AlphaMask);
                for (int b = 0; b < stride; b++) row[x * stride + b] = (byte)(pixel >> (b * 8));
            }
            target.Write(row); // Undefined bits and row padding are deterministic zeroes in rebuilt levels.
        }
    }
}

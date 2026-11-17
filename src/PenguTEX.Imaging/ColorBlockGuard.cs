using System.Buffers.Binary;

namespace PenguTEX.Imaging;

/// <summary>
/// Checks encoded BC1-family colour blocks against their input. The library encoder can let an endpoint
/// overflow on a saturated channel (typically blue 255 in tangent normal maps), which turns a whole 4×4
/// block the wrong colour. Such a block is replaced by a range fit whose endpoints are input colours and
/// therefore cannot overflow. Blocks the library encodes well are left byte-identical.
/// </summary>
public static class ColorBlockGuard
{
    // Four levels RMS per channel over 16 texels. Below this a block is never touched.
    private const long Tolerance = 16 * 3 * 16;

    /// <param name="rgba">One strip of at most four rows, as given to the encoder.</param>
    /// <param name="blocks">The encoded strip: 8-byte BC1 blocks, or 16-byte BC2/BC3 blocks with colour last.</param>
    /// <returns>The number of colour blocks replaced.</returns>
    public static int Repair(ReadOnlySpan<byte> rgba, int width, int rows, Span<byte> blocks, int blockBytes)
    {
        if (blockBytes is not (8 or 16) || rows is < 1 or > 4 || width < 1 || rgba.Length < width * rows * 4
            || blocks.Length != (width + 3) / 4 * blockBytes) throw new ArgumentException("Unexpected strip layout.");
        int replaced = 0; Span<byte> texels = stackalloc byte[48]; Span<byte> fit = stackalloc byte[8];
        for (int block = 0; block < (width + 3) / 4; block++)
        {
            bool opaque = true;
            for (int i = 0; i < 16; i++)
            {
                // Partial blocks repeat their last column and row.
                int at = (Math.Min(i / 4, rows - 1) * width + Math.Min(block * 4 + i % 4, width - 1)) * 4;
                rgba.Slice(at, 3).CopyTo(texels[(i * 3)..]); opaque &= rgba[at + 3] >= 128;
            }
            // BC1 punch-through blocks need the library's three-colour mode; their hidden texels have no colour to match.
            if (blockBytes == 8 && !opaque) continue;
            var colour = blocks.Slice(block * blockBytes + blockBytes - 8, 8);
            long library = Error(texels, colour, alwaysFourColours: blockBytes == 16);
            if (library <= Tolerance) continue;
            RangeFit(texels, fit);
            if (library <= 2 * Error(texels, fit, true) + Tolerance) continue;
            fit.CopyTo(colour); replaced++;
        }
        return replaced;
    }

    private static void Palette(ushort first, ushort second, bool fourColours, Span<int> palette)
    {
        static void Expand(ushort c, Span<int> rgb) { int r = c >> 11, g = (c >> 5) & 63, b = c & 31; rgb[0] = r << 3 | r >> 2; rgb[1] = g << 2 | g >> 4; rgb[2] = b << 3 | b >> 2; }
        Expand(first, palette); Expand(second, palette[3..]);
        for (int c = 0; c < 3; c++)
        {
            int a = palette[c], b = palette[3 + c];
            palette[6 + c] = fourColours ? (2 * a + b + 1) / 3 : (a + b) / 2;
            palette[9 + c] = fourColours ? (a + 2 * b + 1) / 3 : 0;
        }
    }

    private static long Error(ReadOnlySpan<byte> texels, ReadOnlySpan<byte> colour, bool alwaysFourColours)
    {
        ushort first = BinaryPrimitives.ReadUInt16LittleEndian(colour), second = BinaryPrimitives.ReadUInt16LittleEndian(colour[2..]);
        Span<int> palette = stackalloc int[12]; Palette(first, second, alwaysFourColours || first > second, palette);
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(colour[4..]); long error = 0;
        for (int i = 0; i < 16; i++)
        {
            int entry = (int)(indices >> (2 * i)) & 3;
            for (int c = 0; c < 3; c++) { int d = texels[i * 3 + c] - palette[entry * 3 + c]; error += d * d; }
        }
        return error;
    }

    // Endpoints are the two input colours furthest apart along the principal axis; indices are nearest palette entries.
    private static void RangeFit(ReadOnlySpan<byte> texels, Span<byte> colour)
    {
        Span<long> sum = stackalloc long[3]; Span<long> products = stackalloc long[9];
        for (int i = 0; i < 16; i++)
            for (int r = 0; r < 3; r++)
            {
                sum[r] += texels[i * 3 + r];
                for (int c = 0; c < 3; c++) products[r * 3 + c] += texels[i * 3 + r] * texels[i * 3 + c];
            }
        Span<double> covariance = stackalloc double[9];
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) covariance[r * 3 + c] = products[r * 3 + c] - sum[r] * sum[c] / 16.0;
        double x = 1, y = 1, z = 1;
        for (int n = 0; n < 8; n++)
        {
            double nx = covariance[0] * x + covariance[1] * y + covariance[2] * z, ny = covariance[3] * x + covariance[4] * y + covariance[5] * z,
                nz = covariance[6] * x + covariance[7] * y + covariance[8] * z, length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length < 1e-9) break;
            x = nx / length; y = ny / length; z = nz / length;
        }
        int low = 0, high = 0; double minimum = double.MaxValue, maximum = double.MinValue;
        for (int i = 0; i < 16; i++)
        {
            double along = texels[i * 3] * x + texels[i * 3 + 1] * y + texels[i * 3 + 2] * z;
            if (along < minimum) { minimum = along; low = i; }
            if (along > maximum) { maximum = along; high = i; }
        }
        static ushort Pack(ReadOnlySpan<byte> rgb) => (ushort)(((rgb[0] * 31 + 127) / 255) << 11 | ((rgb[1] * 63 + 127) / 255) << 5 | ((rgb[2] * 31 + 127) / 255));
        ushort first = Pack(texels.Slice(high * 3, 3)), second = Pack(texels.Slice(low * 3, 3));
        if (first < second) (first, second) = (second, first);
        // Equal endpoints select BC1's three-colour mode; every texel then uses entry 0, never the transparent one.
        Span<int> palette = stackalloc int[12]; Palette(first, second, true, palette); uint indices = 0;
        for (int i = 0; first != second && i < 16; i++)
        {
            int best = 0; long bestError = long.MaxValue;
            for (int entry = 0; entry < 4; entry++)
            {
                long error = 0;
                for (int c = 0; c < 3; c++) { int d = texels[i * 3 + c] - palette[entry * 3 + c]; error += d * d; }
                if (error < bestError) { bestError = error; best = entry; }
            }
            indices |= (uint)best << (2 * i);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(colour, first); BinaryPrimitives.WriteUInt16LittleEndian(colour[2..], second);
        BinaryPrimitives.WriteUInt32LittleEndian(colour[4..], indices);
    }
}

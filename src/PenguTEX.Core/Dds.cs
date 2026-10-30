using System.Buffers.Binary;

namespace PenguTEX.Core;

public sealed record DdsInfo(int Width, int Height, int MipCount, int BlockBytes, string Format,
    int Dxgi, int HeaderBytes, long PayloadBytes, bool Srgb, bool Signed)
{
    public bool Premultiplied { get; init; }
    public int BitsPerPixel { get; init; }
    public int RowAlignment { get; init; } = 1;
    public uint RedMask { get; init; }
    public uint GreenMask { get; init; }
    public uint BlueMask { get; init; }
    public uint AlphaMask { get; init; }
    public bool Luminance { get; init; }
    public int? DeclaredMipCount { get; init; }
    public string? RecordLayout { get; init; }
    public bool IsBlockCompressed => BlockBytes != 0;
    public bool HasByteChannels => !IsBlockCompressed && !Signed && (RedMask | GreenMask | BlueMask | AlphaMask) != 0
        && new[] { RedMask, GreenMask, BlueMask, AlphaMask }.All(m => System.Numerics.BitOperations.PopCount(m) <= 8);
    public long RowBytes(int mip) => IsBlockCompressed
        ? checked((long)((Math.Max(1, Width >> mip) + 3) / 4) * BlockBytes)
        : checked((((long)Math.Max(1, Width >> mip) * BitsPerPixel + 7) / 8 + RowAlignment - 1) / RowAlignment * RowAlignment);
    public long MipBytes(int mip) => checked(RowBytes(mip) * (IsBlockCompressed
        ? (Math.Max(1, Height >> mip) + 3) / 4 : Math.Max(1, Height >> mip)));
    public long RemovedBytes(int drop) => Enumerable.Range(0, drop).Sum(MipBytes);
    public int DropFor(int maximum)
    {
        int drop = 0;
        while (drop < MipCount - 1 && Math.Max(Math.Max(1, Width >> drop), Math.Max(1, Height >> drop)) > maximum) drop++;
        return drop;
    }
}

public sealed class ProtectedTextureException(string message) : NotSupportedException(message);

public static class Dds
{
    // For validated record recovery on a forward-only stream, inspection consumes the records.
    public static (DdsInfo Info, byte[] Header) Inspect(Stream input, long? length = null, bool recoverMipCount = false,
        bool recoverMipRecords = false, CancellationToken ct = default)
        => InspectCore(input, length, recoverMipCount, recoverMipRecords, validateRecords: true, ct);

    private static (DdsInfo Info, byte[] Header) InspectCore(Stream input, long? length, bool recoverMipCount,
        bool recoverMipRecords, bool validateRecords, CancellationToken ct)
    {
        var header = new byte[128]; input.ReadExactly(header);
        uint U(int at) => BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(at));
        if (!header.AsSpan(0, 4).SequenceEqual("DDS "u8) || U(4) != 124 || U(76) != 32)
            throw new InvalidDataException("Invalid DDS magic or header size.");
        if ((U(8) & 0x1007) != 0x1007 || (U(108) & 0x1000) == 0)
            throw new InvalidDataException("DDS is missing required texture/header flags.");
        uint width = U(16), height = U(12), rawMips = U(28);
        if (width == 0 || height == 0 || width > 16384 || height > 16384)
            throw new InvalidDataException("DDS dimensions must be between 1 and 16384.");
        int mips = (U(8) & 0x20000) == 0 ? 1 : (int)Math.Max(1, rawMips);
        if (mips < 1 || mips > 1 + (int)Math.Log2(Math.Max(width, height)))
            throw new InvalidDataException("DDS mip count exceeds its dimensions.");
        if ((U(112) & (0xfe00 | 0x200000)) != 0 || U(24) > 1)
            throw new ProtectedTextureException("Cube maps and volume textures are deliberately protected; their surfaces are not assessed or rewritten.");
        string cc = System.Text.Encoding.ASCII.GetString(header, 84, 4);
        int dxgi = 0, block = 0, bits = 0; bool srgb = false, signed = false, luminance = false; string format;
        uint red = 0, green = 0, blue = 0, alpha = 0;
        if ((U(80) & 4) != 0 && cc == "DX10")
        {
            Array.Resize(ref header, 148); input.ReadExactly(header.AsSpan(128));
            if (U(132) != 3 || U(140) != 1 || (U(136) & 4) != 0)
                throw new ProtectedTextureException("Texture arrays and non-2D resources are deliberately protected; their surfaces are not assessed or rewritten.");
            dxgi = checked((int)U(128));
            (format, block) = dxgi switch
            {
                71 or 72 => ("BC1", 8), 74 or 75 => ("BC2", 16), 77 or 78 => ("BC3", 16),
                80 or 81 => ("BC4", 8), 83 or 84 => ("BC5", 16), 95 or 96 => ("BC6H", 16), 98 or 99 => ("BC7", 16),
                _ => ("DXGI " + dxgi, 0),
            };
            if (block == 0)
            {
                bits = dxgi switch
                {
                    2 => 128, 10 or 11 or 16 => 64,
                    24 or 26 or 28 or 29 or 31 or 34 or 35 or 37 or 41 or 67 or 87 or 88 or 91 or 93 => 32,
                    49 or 51 or 54 or 56 or 58 or 85 or 86 or 115 => 16,
                    61 or 63 or 65 => 8,
                    _ => throw new NotSupportedException($"DXGI format {dxgi} is not supported by this operation."),
                };
                (red, green, blue, alpha) = dxgi switch
                {
                    28 or 29 => (0xffu, 0xff00u, 0xff0000u, 0xff000000u),
                    87 or 91 => (0xff0000u, 0xff00u, 0xffu, 0xff000000u),
                    88 or 93 => (0xff0000u, 0xff00u, 0xffu, 0u),
                    49 => (0xffu, 0xff00u, 0u, 0u), 61 => (0xffu, 0u, 0u, 0u), 65 => (0u, 0u, 0u, 0xffu),
                    85 => (0xf800u, 0x7e0u, 0x1fu, 0u), 86 => (0x7c00u, 0x3e0u, 0x1fu, 0x8000u),
                    115 => (0xf00u, 0xf0u, 0xfu, 0xf000u),
                    _ => (0u, 0u, 0u, 0u),
                };
            }
            srgb = dxgi is 29 or 72 or 75 or 78 or 91 or 93 or 99;
            signed = dxgi is 31 or 37 or 51 or 58 or 63 or 81 or 84 or 96;
        }
        else if ((U(80) & 4) != 0)
        {
            (format, block) = cc switch
            {
                "DXT1" => ("BC1", 8), "DXT2" or "DXT3" => ("BC2", 16), "DXT4" or "DXT5" => ("BC3", 16),
                "ATI1" or "BC4U" => ("BC4", 8), "BC4S" => ("BC4", 8),
                "ATI2" or "BC5U" => ("BC5", 16), "BC5S" => ("BC5", 16),
                _ => throw new NotSupportedException($"DDS FourCC '{cc}' is unsupported."),
            };
            signed = cc is "BC4S" or "BC5S";
        }
        else
        {
            uint pixelFlags = U(80);
            if ((pixelFlags & ~(0x40u | 0x20000u | 0x2u | 0x1u)) != 0
                || (pixelFlags & (0x40u | 0x20000u | 0x2u)) == 0)
                throw new NotSupportedException("Unsupported uncompressed DDS pixel flags.");
            bits = checked((int)U(88)); red = U(92); green = U(96); blue = U(100); alpha = U(104);
            if (bits is not (8 or 16 or 24 or 32)) throw new NotSupportedException("Unsupported uncompressed DDS bit depth.");
            uint used = 0;
            foreach (uint mask in new[] { red, green, blue, alpha })
            {
                if (mask == 0) continue;
                uint shifted = mask >> System.Numerics.BitOperations.TrailingZeroCount(mask);
                if ((used & mask) != 0 || (bits < 32 && (mask >> bits) != 0) || (shifted & unchecked(shifted + 1)) != 0)
                    throw new InvalidDataException("Invalid or overlapping DDS channel masks.");
                used |= mask;
            }
            if (used == 0 || ((pixelFlags & 3) != 0 && alpha == 0)) throw new InvalidDataException("Missing DDS channel masks.");
            luminance = (pixelFlags & 0x20000) != 0;
            if (luminance && (red == 0 || green != 0 || blue != 0)) throw new NotSupportedException("Unsupported luminance layout.");
            format = (luminance ? "Luminance" : "Raw") + bits;
        }
        var info = new DdsInfo((int)width, (int)height, mips, block, format, dxgi, header.Length, 0, srgb, signed)
        { Premultiplied = cc is "DXT2" or "DXT4" || (header.Length == 148 && (U(144) & 7) == 2),
            BitsPerPixel = bits, RedMask = red, GreenMask = green, BlueMask = blue, AlphaMask = alpha, Luminance = luminance };
        long payload = Enumerable.Range(0, mips).Sum(info.MipBytes);
        long? actualLength = length ?? (input.CanSeek ? input.Length : null);
        if (recoverMipCount && info.IsBlockCompressed && actualLength.HasValue && actualLength.Value != header.Length + payload)
        {
            int fullCount = 1 + (int)Math.Log2(Math.Max(width, height));
            long fullPayload = Enumerable.Range(0, fullCount).Sum(info.MipBytes);
            if (mips < fullCount && actualLength.Value == header.Length + fullPayload)
            { info = info with { MipCount = fullCount, DeclaredMipCount = mips }; payload = fullPayload; }
        }
        if (recoverMipRecords && cc == "DXT1" && info.HeaderBytes == 128 && mips == 1 && actualLength.HasValue)
        {
            int fullCount = 1 + (int)Math.Log2(Math.Max(width, height));
            long fullPayload = Enumerable.Range(0, fullCount).Sum(info.MipBytes);
            long framedLength = checked(header.Length + fullPayload + 16L * (fullCount - 1) + 5);
            if (fullCount > 1 && actualLength.Value == framedLength)
            {
                info = info with { MipCount = fullCount, DeclaredMipCount = mips, RecordLayout = "BC1-mip-records16-zero-tail5" };
                payload = fullPayload;
                if (validateRecords)
                {
                    long? start = input.CanSeek ? input.Position : null;
                    CopyMipRecords(input, Stream.Null, info, 0, ct);
                    if (start is { } position) input.Position = position;
                }
            }
        }
        if (!info.IsBlockCompressed)
        {
            // Legacy writers use either tight rows or DWORD-aligned rows, including mip tails.
            // Infer only a layout justified by the complete payload; the top-level pitch alone is ambiguous.
            if (!actualLength.HasValue) throw new NotSupportedException("Uncompressed DDS inspection requires the complete file length.");
            if (actualLength.Value != header.Length + payload)
            {
                var aligned = info with { RowAlignment = 4 };
                long padded = Enumerable.Range(0, mips).Sum(aligned.MipBytes);
                if (actualLength.Value == header.Length + padded) { info = aligned; payload = padded; }
            }
        }
        if (info.RecordLayout is null && actualLength.HasValue && actualLength.Value != header.Length + payload)
            throw new InvalidDataException($"DDS payload length is {actualLength.Value - header.Length}; expected {payload}. Unrecognized trailing or missing bytes require a validated original conversion.");
        return (info with { PayloadBytes = payload }, header);
    }

    public static void Reduce(Stream source, Stream target, int drop, long length, CancellationToken ct = default, DdsInfo? expected = null)
    {
        var (info, header) = InspectCore(source, length, expected?.DeclaredMipCount is not null, expected?.RecordLayout is not null, validateRecords: false, ct);
        if (expected is not null && info != expected) throw new IOException("Source metadata changed; analyze again.");
        if (drop < 0 || drop >= info.MipCount || drop == 0 && info.DeclaredMipCount is null) throw new ArgumentOutOfRangeException(nameof(drop));
        ResizeHeader(header, info, Math.Max(1, info.Width >> drop), Math.Max(1, info.Height >> drop), info.MipCount - drop);
        target.Write(header);
        if (info.RecordLayout is not null)
        { CopyMipRecords(source, target, info, drop, ct); return; }
        Transfer(source, Stream.Null, info.RemovedBytes(drop), ct);
        Transfer(source, target, info.PayloadBytes - info.RemovedBytes(drop), ct);
        if (source.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing DDS payload.");
    }

    private static void CopyMipRecords(Stream source, Stream target, DdsInfo info, int drop, CancellationToken ct)
    {
        Span<byte> descriptor = stackalloc byte[16];
        for (int level = 0; level < info.MipCount; level++)
        {
            ct.ThrowIfCancellationRequested();
            if (level > 0)
            {
                source.ReadExactly(descriptor);
                uint width = BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
                uint height = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[4..]);
                uint pitch = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[8..]);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..]);
                if (width != Math.Max(1, info.Width >> level) || height != Math.Max(1, info.Height >> level)
                    || pitch != info.RowBytes(level) || size != info.MipBytes(level))
                    throw new InvalidDataException($"Invalid DDS mip record {level}: dimensions, row pitch or slice length disagree. Restore the original or use a validated source conversion.");
            }
            Transfer(source, level >= drop ? target : Stream.Null, info.MipBytes(level), ct);
        }
        Span<byte> footer = stackalloc byte[5]; source.ReadExactly(footer);
        if (footer.ContainsAnyExcept((byte)0) || source.ReadByte() != -1)
            throw new InvalidDataException("Unrecognized DDS mip-record footer. Trailing bytes are not discarded.");
    }

    public static void ResizeHeader(byte[] header, DdsInfo info, int width, int height, int mips)
    {
        if (width < 1 || height < 1 || width > 16384 || height > 16384 || mips < 1 || mips > 1 + (int)Math.Log2(Math.Max(width, height)))
            throw new ArgumentException("Invalid output dimensions/mip count.");
        void Set(int at, uint n) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(at), n);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        uint caps = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(108));
        var output = info with { Width = width, Height = height };
        Set(12, (uint)height); Set(16, (uint)width); Set(20, checked((uint)(info.IsBlockCompressed ? output.MipBytes(0) : output.RowBytes(0)))); Set(28, (uint)mips);
        Set(8, (flags & ~(8u | 0x80000u | 0x20000u)) | (info.IsBlockCompressed ? 0x80000u : 8u) | (mips > 1 ? 0x20000u : 0u));
        Set(108, mips > 1 ? caps | 0x8u | 0x400000u : caps & ~(0x8u | 0x400000u));
    }

    public static void Transfer(Stream from, Stream to, long bytes, CancellationToken ct)
    {
        byte[] buffer = new byte[128 * 1024];
        while (bytes > 0)
        {
            ct.ThrowIfCancellationRequested(); int n = from.Read(buffer, 0, (int)Math.Min(buffer.Length, bytes));
            if (n == 0) throw new EndOfStreamException("Truncated texture payload.");
            to.Write(buffer, 0, n); bytes -= n;
        }
    }
}

using System.Buffers.Binary;

namespace PenguTEX.Core;

public sealed record DdsInfo(int Width, int Height, int MipCount, int BlockBytes, string Format,
    int Dxgi, int HeaderBytes, long PayloadBytes, bool Srgb, bool Signed)
{
    public bool Premultiplied { get; init; }
    public long MipBytes(int mip) => checked((long)((Math.Max(1, Width >> mip) + 3) / 4)
        * ((Math.Max(1, Height >> mip) + 3) / 4) * BlockBytes);
    public long RemovedBytes(int drop) => Enumerable.Range(0, drop).Sum(MipBytes);
    public int DropFor(int maximum)
    {
        int drop = 0;
        while (drop < MipCount - 1 && Math.Max(Math.Max(1, Width >> drop), Math.Max(1, Height >> drop)) > maximum) drop++;
        return drop;
    }
}

public static class Dds
{
    public static (DdsInfo Info, byte[] Header) Inspect(Stream input, long? length = null)
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
            throw new NotSupportedException("Cube maps and volume textures are protected.");
        if ((U(80) & 4) == 0) throw new NotSupportedException("This mode requires a block-compressed DDS.");
        string cc = System.Text.Encoding.ASCII.GetString(header, 84, 4);
        int dxgi = 0, block; bool srgb = false, signed = false; string format;
        if (cc == "DX10")
        {
            Array.Resize(ref header, 148); input.ReadExactly(header.AsSpan(128));
            if (U(132) != 3 || U(140) != 1 || (U(136) & 4) != 0)
                throw new NotSupportedException("Only single-surface 2D textures can be processed.");
            dxgi = checked((int)U(128));
            (format, block) = dxgi switch
            {
                71 or 72 => ("BC1", 8), 74 or 75 => ("BC2", 16), 77 or 78 => ("BC3", 16),
                80 or 81 => ("BC4", 8), 83 or 84 => ("BC5", 16), 98 or 99 => ("BC7", 16),
                _ => throw new NotSupportedException($"DXGI format {dxgi} is not supported by this operation."),
            };
            srgb = dxgi is 72 or 75 or 78 or 99; signed = dxgi is 81 or 84;
        }
        else
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
        var info = new DdsInfo((int)width, (int)height, mips, block, format, dxgi, header.Length, 0, srgb, signed)
        { Premultiplied = cc is "DXT2" or "DXT4" || (cc == "DX10" && (U(144) & 7) == 2) };
        long payload = Enumerable.Range(0, mips).Sum(info.MipBytes);
        long? actualLength = length ?? (input.CanSeek ? input.Length : null);
        if (actualLength.HasValue && actualLength.Value != header.Length + payload)
            throw new InvalidDataException($"DDS payload length is {actualLength.Value - header.Length}; expected {payload}.");
        return (info with { PayloadBytes = payload }, header);
    }

    public static void Reduce(Stream source, Stream target, int drop, long length, CancellationToken ct = default, DdsInfo? expected = null)
    {
        var (info, header) = Inspect(source, length);
        if (expected is not null && info != expected) throw new IOException("Source metadata changed; analyze again.");
        if (drop <= 0 || drop >= info.MipCount) throw new ArgumentOutOfRangeException(nameof(drop));
        ResizeHeader(header, info, Math.Max(1, info.Width >> drop), Math.Max(1, info.Height >> drop), info.MipCount - drop);
        target.Write(header);
        Transfer(source, Stream.Null, info.RemovedBytes(drop), ct);
        Transfer(source, target, info.PayloadBytes - info.RemovedBytes(drop), ct);
        if (source.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing DDS payload.");
    }

    public static void ResizeHeader(byte[] header, DdsInfo info, int width, int height, int mips)
    {
        if (width < 1 || height < 1 || width > 16384 || height > 16384 || mips < 1 || mips > 1 + (int)Math.Log2(Math.Max(width, height)))
            throw new ArgumentException("Invalid output dimensions/mip count.");
        void Set(int at, uint n) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(at), n);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        uint caps = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(108));
        var output = info with { Width = width, Height = height };
        Set(12, (uint)height); Set(16, (uint)width); Set(20, checked((uint)output.MipBytes(0))); Set(28, (uint)mips);
        Set(8, (flags & ~(8u | 0x20000u)) | 0x80000u | (mips > 1 ? 0x20000u : 0u));
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

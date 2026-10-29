using System.Buffers.Binary;
using PenguTEX.Core;

namespace PenguTEX.Tests;

public sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "pengutex-test-" + Guid.NewGuid().ToString("N"));
    public Fixture() => Directory.CreateDirectory(Root);
    public string Dir(string name) { string p = Path.Combine(Root, name); Directory.CreateDirectory(p); return p; }
    public string File(string name, byte[] data) { string p = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(p)!); System.IO.File.WriteAllBytes(p, data); return p; }
    public string Text(string name, string data) => File(name, System.Text.Encoding.UTF8.GetBytes(data));
    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
    public static byte[] Dds(int width = 16, int height = 16, int dxgi = 98, string? fourCC = null, int? mipCount = null)
    {
        int mips = mipCount ?? 1 + (int)Math.Log2(Math.Max(width, height));
        int block = dxgi is 71 or 72 or 80 or 81 || fourCC is "DXT1" or "BC4U" or "BC4S" ? 8 : 16;
        int header = fourCC is null ? 148 : 128;
        var info = new DdsInfo(width, height, mips, block, "", dxgi, header, 0, false, false);
        byte[] bytes = new byte[header + Enumerable.Range(0, mips).Sum(info.MipBytes)];
        void W(int at, uint n) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), n);
        "DDS "u8.CopyTo(bytes); W(4, 124); W(8, 0xA1007); W(12, (uint)height); W(16, (uint)width);
        W(20, (uint)info.MipBytes(0)); W(28, (uint)mips); W(76, 32); W(80, 4); W(108, 0x401008);
        System.Text.Encoding.ASCII.GetBytes(fourCC ?? "DX10").CopyTo(bytes, 84);
        if (fourCC is null) { W(128, (uint)dxgi); W(132, 3); W(140, 1); }
        for (int i = header; i < bytes.Length; i++) bytes[i] = (byte)(i * 31);
        return bytes;
    }
    public static byte[] Bsa(byte[] payload, string filename = "archive.dds")
    {
        using var output = new MemoryStream(); using var w = new BinaryWriter(output);
        byte[] folder = System.Text.Encoding.UTF8.GetBytes("textures\0"), name = System.Text.Encoding.UTF8.GetBytes(filename + "\0");
        const uint folderBlock = 60;
        uint dataOffset = folderBlock + 1 + (uint)folder.Length + 16 + (uint)name.Length;
        w.Write("BSA\0"u8); w.Write(105u); w.Write(36u); w.Write(3u); w.Write(1u); w.Write(1u);
        w.Write((uint)folder.Length); w.Write((uint)name.Length); w.Write(2u);
        w.Write(0UL); w.Write(1u); w.Write(0u); w.Write((ulong)(folderBlock + name.Length));
        w.Write((byte)folder.Length); w.Write(folder); w.Write(0UL); w.Write((uint)payload.Length); w.Write(dataOffset);
        w.Write(name); w.Write(payload); return output.ToArray();
    }
}

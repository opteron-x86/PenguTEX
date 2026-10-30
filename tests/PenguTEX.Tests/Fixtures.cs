using System.Buffers.Binary;
using System.Text;
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
    // Metadata-only synthetic SSE NIF: no game geometry or third-party textures.
    public static byte[] Mesh(int slot = 0, string texture = "textures/a.dds", uint type = 0,
        ushort? alpha = null, byte threshold = 128, uint flags1 = 0, uint flags2 = 0, bool bound = true)
    {
        static byte[] Bytes(Action<BinaryWriter> write) { using var s = new MemoryStream(); using var w = new BinaryWriter(s); write(w); return s.ToArray(); }
        static void Net(BinaryWriter w) { w.Write(-1); w.Write(0u); w.Write(-1); }
        static void Str(BinaryWriter w, string s) { byte[] b = Encoding.Latin1.GetBytes(s); w.Write(b.Length); w.Write(b); }
        var blocks = new List<(string Type, byte[] Data)>
        {
            ("BSShaderTextureSet", Bytes(w => { w.Write(9u); for (int i = 0; i < 9; i++) Str(w, i == slot ? texture : ""); })),
            ("BSLightingShaderProperty", Bytes(w => { w.Write(type); Net(w); w.Write(flags1); w.Write(flags2); w.Write(new byte[16]); w.Write(0); w.Write(new byte[20]); w.Write(1f); })),
            ("BSTriShape", Bytes(w => { Net(w); w.Write(new byte[4 + 52]); w.Write(-1); w.Write(new byte[16]); w.Write(-1); w.Write(bound ? 1 : -1); w.Write(alpha.HasValue ? 3 : -1); })),
        };
        if (alpha is { } a) blocks.Add(("NiAlphaProperty", Bytes(w => { Net(w); w.Write(a); w.Write(threshold); })));
        return Bytes(w =>
        {
            w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            w.Write(0x14020007u); w.Write((byte)1); w.Write(12u); w.Write(blocks.Count); w.Write(100u); w.Write(new byte[3]);
            w.Write((ushort)blocks.Count); foreach (var b in blocks) Str(w, b.Type);
            for (ushort i = 0; i < blocks.Count; i++) w.Write(i);
            foreach (var b in blocks) w.Write(b.Data.Length);
            w.Write(0u); w.Write(0u); w.Write(0u);
            foreach (var b in blocks) w.Write(b.Data);
            w.Write(1u); w.Write(2);
        });
    }
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
    public static byte[] Bsa(byte[] payload, string filename = "archive.dds", string directory = "textures")
    {
        using var output = new MemoryStream(); using var w = new BinaryWriter(output);
        byte[] folder = System.Text.Encoding.UTF8.GetBytes(directory + "\0"), name = System.Text.Encoding.UTF8.GetBytes(filename + "\0");
        const uint folderBlock = 60;
        uint dataOffset = folderBlock + 1 + (uint)folder.Length + 16 + (uint)name.Length;
        w.Write("BSA\0"u8); w.Write(105u); w.Write(36u); w.Write(3u); w.Write(1u); w.Write(1u);
        w.Write((uint)folder.Length); w.Write((uint)name.Length); w.Write(2u);
        w.Write(0UL); w.Write(1u); w.Write(0u); w.Write((ulong)(folderBlock + name.Length));
        w.Write((byte)folder.Length); w.Write(folder); w.Write(0UL); w.Write((uint)payload.Length); w.Write(dataOffset);
        w.Write(name); w.Write(payload); return output.ToArray();
    }
    public static byte[] RawDds(int width = 16, int height = 8, int bits = 32, int alignment = 1, int? dxgi = null, int? mipCount = null)
    {
        int mips = mipCount ?? 1 + (int)Math.Log2(Math.Max(width, height)), header = dxgi.HasValue ? 148 : 128;
        int Pitch(int w) => ((w * bits + 7) / 8 + alignment - 1) / alignment * alignment;
        int payload = Enumerable.Range(0, mips).Sum(i => Pitch(Math.Max(1, width >> i)) * Math.Max(1, height >> i));
        var data = new byte[header + payload];
        void W(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
        "DDS "u8.CopyTo(data); W(4, 124); W(8, 0x2100f); W(12, (uint)height); W(16, (uint)width); W(20, (uint)Pitch(width));
        W(28, (uint)mips); W(76, 32); W(108, 0x401008);
        if (dxgi.HasValue) { W(80, 4); "DX10"u8.CopyTo(data.AsSpan(84)); W(128, (uint)dxgi.Value); W(132, 3); W(140, 1); }
        else
        {
            W(80, bits == 8 ? 0x20000u : bits == 32 ? 0x41u : 0x40u); W(88, (uint)bits);
            W(92, bits == 16 ? 0xf800u : 0xffu); W(96, bits == 8 ? 0u : bits == 16 ? 0x7e0u : 0xff00u);
            W(100, bits == 8 ? 0u : bits == 16 ? 0x1fu : 0xff0000u); W(104, bits == 32 ? 0xff000000u : 0u);
        }
        for (int i = header; i < data.Length; i++) data[i] = (byte)(i * 37 + 3);
        return data;
    }
}

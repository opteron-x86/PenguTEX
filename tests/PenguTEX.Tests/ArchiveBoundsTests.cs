using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4.Streams;
using PenguTEX.Assets.Archives;
using PenguTEX.Core;

namespace PenguTEX.Tests;

public sealed class ArchiveBoundsTests
{
    public static byte[] Archive(byte[][] payloads, bool compressed = false, bool embedded = false, uint version = 105, int sizeDelta = 0, bool rawFallback = false)
    {
        var names = payloads.Select((_, i) => Encoding.ASCII.GetBytes($"{i}.dds\0")).ToArray();
        var stored = payloads.Select((data, i) =>
        {
            using var output = new MemoryStream(); using var w = new BinaryWriter(output);
            if (embedded) { var n = Encoding.ASCII.GetBytes($"textures\\{i}.dds"); w.Write((byte)n.Length); w.Write(n); }
            if (compressed)
            {
                w.Write(data.Length + sizeDelta);
                if (rawFallback) w.Write(data);
                else
                {
                    using Stream encoder = version == 105 ? LZ4Stream.Encode(output, leaveOpen: true)
                        : new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
                    encoder.Write(data);
                }
            }
            else w.Write(data);
            return output.ToArray();
        }).ToArray();
        using var result = new MemoryStream(); using var writer = new BinaryWriter(result);
        byte[] folder = "textures\0"u8.ToArray(); int folderBlock = version == 105 ? 60 : 52;
        int dataOffset = folderBlock + 1 + folder.Length + 16 * payloads.Length + names.Sum(n => n.Length);
        writer.Write("BSA\0"u8); writer.Write(version); writer.Write(36u); writer.Write(3u | (compressed ? 4u : 0u) | (embedded ? 256u : 0u));
        writer.Write(1u); writer.Write(payloads.Length); writer.Write(folder.Length); writer.Write(names.Sum(n => n.Length)); writer.Write(2u);
        writer.Write(0UL); writer.Write(payloads.Length);
        if (version == 105) { writer.Write(0u); writer.Write((ulong)(folderBlock + names.Sum(n => n.Length))); }
        else writer.Write((uint)(folderBlock + names.Sum(n => n.Length)));
        writer.Write((byte)folder.Length); writer.Write(folder);
        foreach (byte[] data in stored) { writer.Write(0UL); writer.Write(data.Length); writer.Write(dataOffset); dataOffset += data.Length; }
        foreach (var name in names) writer.Write(name); foreach (var data in stored) writer.Write(data);
        return result.ToArray();
    }
    [Fact] public void UnreferencedMetadataBytesAreReportedWithoutChangingPayloadBoundaries()
    {
        using var f = new Fixture(); byte[] original = Fixture.Dds(), archive = Archive([original]);
        const int offsetAt = 60 + 1 + 9 + 12;
        int start = (int)BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(offsetAt));
        byte[] padded = [..archive[..start], 0x41, 0x42, 0x43, ..archive[start..]];
        BinaryPrimitives.WriteUInt32LittleEndian(padded.AsSpan(28), BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(28)) + 3);
        BinaryPrimitives.WriteUInt64LittleEndian(padded.AsSpan(52), BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(52)) + 3);
        BinaryPrimitives.WriteUInt32LittleEndian(padded.AsSpan(offsetAt), (uint)(start + 3));
        var diagnostics = new List<Diagnostic>(); var entry = Assert.Single(BoundedBsa.Read(f.File("test.bsa", padded), diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("unused filename-table"));
        using var input = entry.Open(); using var output = new MemoryStream(); input.CopyTo(output); Assert.Equal(original, output.ToArray());
    }
    [Theory] [InlineData(false, false, 105)] [InlineData(false, true, 105)] [InlineData(true, false, 105)]
    [InlineData(true, true, 105)] [InlineData(true, false, 104)] [InlineData(false, false, 104)]
    public void BoundedMembersRoundTripWithoutLeakingNeighbors(bool compressed, bool embedded, uint version)
    {
        using var f = new Fixture(); byte[] a = Fixture.Dds(), b = Fixture.Dds(32);
        var members = BoundedBsa.Read(f.File("test.bsa", Archive([a, b], compressed, embedded, version)));
        Assert.Equal(2, members.Count);
        for (int i = 0; i < members.Count; i++) { using var source = members[i].Open(); using var copy = new MemoryStream(); source.CopyTo(copy); Assert.Equal(i == 0 ? a : b, copy.ToArray()); Assert.Equal(-1, source.ReadByte()); }
    }
    [Fact] public void OverlappingMemberIsRejectedWithoutInvalidatingOtherOriginals()
    {
        using var f = new Fixture(); byte[] data = Archive([Fixture.Dds(), Fixture.Dds()]);
        const int firstSize = 60 + 1 + 9 + 8; uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(firstSize));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(firstSize), size + 1);
        var entries = BoundedBsa.Read(f.File("test.bsa", data));
        var error = Assert.Throws<InvalidDataException>(() => entries[0].Open()); Assert.Contains("overlaps the next member", error.Message);
        using var good = entries[1].Open(); Assert.Equal(16, Dds.Inspect(good, entries[1].Size).Info.Width);
    }
    [Fact] public void RejectsDuplicateOffsetsAndOutOfFileBounds()
    {
        using var f = new Fixture(); byte[] data = Archive([Fixture.Dds(), Fixture.Dds()]);
        const int offset = 60 + 1 + 9 + 12; uint first = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 16), first);
        var entries = BoundedBsa.Read(f.File("duplicates.bsa", data)); Assert.All(entries, e => Assert.Throws<InvalidDataException>(() => e.Open()));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), (uint)data.Length + 1);
        entries = BoundedBsa.Read(f.File("outside.bsa", data)); Assert.Throws<InvalidDataException>(() => entries[0].Open());
    }
    [Theory] [InlineData(-1)] [InlineData(1)]
    public void DecodedSizeMismatchIsAnArchiveDiagnostic(int delta)
    {
        using var f = new Fixture(); var entry = Assert.Single(BoundedBsa.Read(f.File("test.bsa", Archive([Fixture.Dds()], true, sizeDelta: delta))));
        var e = Assert.Throws<InvalidDataException>(() => { using var stream = entry.Open(); stream.CopyTo(Stream.Null); });
        Assert.Contains("Archive", e.Message);
    }
    [Fact] public void TruncatedCompressionFramesCannotReadAdjacentMembers()
    {
        using var f = new Fixture(); byte[] data = Archive([Fixture.Dds(), Fixture.Dds()], true);
        const int sizeAt = 60 + 1 + 9 + 8; uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(sizeAt));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sizeAt), size - 2);
        var entries = BoundedBsa.Read(f.File("test.bsa", data));
        Assert.Contains("compression bounds", Assert.Throws<InvalidDataException>(() => entries[0].Open()).Message);
    }
    [Fact] public void SseEqualLengthRawFallbackAndMetadataChangesAreHandled()
    {
        using var f = new Fixture(); byte[] original = Fixture.Dds();
        string path = f.File("test.bsa", Archive([original], true, true, rawFallback: true));
        var entry = Assert.Single(BoundedBsa.Read(path));
        using (var s = entry.Open()) { using var copy = new MemoryStream(); s.CopyTo(copy); Assert.Equal(original, copy.ToArray()); }
        var changed = File.ReadAllBytes(path); changed[60 + 1 + 9 + 8] ^= 1; File.WriteAllBytes(path, changed);
        Assert.Throws<IOException>(() => entry.Open());
    }
}

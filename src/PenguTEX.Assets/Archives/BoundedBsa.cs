using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4.Streams;
using PenguTEX.Assets.Paths;
using PenguTEX.Core;

namespace PenguTEX.Assets.Archives;

// Keep the physical member boundary separate from the decoded DDS boundary.
public sealed record BsaMember(string Path, long Size, Func<Stream> Open);
public static class BoundedBsa
{
    private sealed record Record(string Folder, uint RawSize, uint Offset, long Position);
    public static IReadOnlyList<BsaMember> Read(string archive, ICollection<Diagnostic>? diagnostics = null)
    {
        using var file = File.OpenRead(archive); using var r = new BinaryReader(file, Encoding.Latin1);
        long archiveLength = file.Length;
        if (r.ReadUInt32() != 0x00415342) throw new InvalidDataException("Archive header is not BSA.");
        uint version = r.ReadUInt32(); if (version is not (104 or 105)) throw new NotSupportedException("Archive bounds validation supports BSA 104/105.");
        uint folderStart = r.ReadUInt32(), flags = r.ReadUInt32(), folderCount = r.ReadUInt32(), fileCount = r.ReadUInt32();
        uint folderNames = r.ReadUInt32(), fileNames = r.ReadUInt32(); r.ReadUInt32();
        if ((flags & 3) != 3 || folderCount > 1000000 || fileCount > 1000000 || folderNames > 128 * 1024 * 1024 || fileNames > 128 * 1024 * 1024)
            throw new InvalidDataException("Archive name tables are absent or exceed supported inspection bounds.");
        int folderRecordBytes = version == 105 ? 24 : 16;
        long tableEnd = checked(folderStart + (long)folderCount * folderRecordBytes);
        if (folderStart < 36 || tableEnd > archiveLength) throw new InvalidDataException("Archive folder table is outside the file.");
        file.Position = folderStart; var folders = new List<(uint Count, long Offset)>(); long total = 0;
        for (int i = 0; i < folderCount; i++)
        {
            r.ReadUInt64(); uint count = r.ReadUInt32(); if (version == 105) r.ReadUInt32();
            ulong offset = version == 105 ? r.ReadUInt64() : r.ReadUInt32();
            if (offset > long.MaxValue || (long)offset < fileNames) throw new InvalidDataException("Archive folder offset is invalid.");
            folders.Add((count, checked((long)offset - fileNames))); total += count;
        }
        if (total != fileCount) throw new InvalidDataException("Archive file counts disagree.");
        var records = new List<Record>(); long end = tableEnd, namesRead = 0;
        foreach (var folder in folders)
        {
            if (folder.Offset != end) throw new InvalidDataException("Archive folder blocks overlap or have unsupported gaps/order.");
            file.Position = folder.Offset; int count = r.ReadByte();
            if (count == 0) throw new InvalidDataException("Archive folder name is empty.");
            byte[] name = r.ReadBytes(count); if (name.Length != count || name[^1] != 0) throw new InvalidDataException("Archive folder name is truncated or unterminated.");
            string directory = Encoding.Latin1.GetString(name.AsSpan(0, count - 1)); namesRead += count;
            if (file.Position + folder.Count * 16L > archiveLength) throw new InvalidDataException("Archive member table is truncated.");
            for (int i = 0; i < folder.Count; i++)
            { long position = file.Position; r.ReadUInt64(); records.Add(new(directory, r.ReadUInt32(), r.ReadUInt32(), position)); }
            end = file.Position;
        }
        if (namesRead != folderNames || end + fileNames > archiveLength) throw new InvalidDataException("Archive name-table lengths disagree.");
        file.Position = end; byte[] names = r.ReadBytes(checked((int)fileNames)); int nameAt = 0;
        long metadataEnd = end + fileNames;
        var sorted = records.OrderBy(e => e.Offset).ToArray();
        var bounds = new Dictionary<long, string?>();
        for (int i = 0; i < sorted.Length; i++)
        {
            var e = sorted[i]; long size = e.RawSize & 0x3fffffff;
            long next = i + 1 < sorted.Length ? sorted[i + 1].Offset : archiveLength;
            bounds[e.Position] = (e.RawSize & 0x80000000) != 0 ? "unknown size flag"
                : e.Offset < metadataEnd ? "payload overlaps archive metadata"
                : e.Offset > archiveLength || size > archiveLength - e.Offset ? "payload exceeds archive file"
                : (i > 0 && sorted[i - 1].Offset == e.Offset) || next == e.Offset ? "duplicate payload offsets"
                : e.Offset + size > next ? "declared member overlaps the next member" : null;
        }
        var result = new List<BsaMember>(); var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            int zero = Array.IndexOf(names, (byte)0, nameAt);
            if (zero < 0) throw new InvalidDataException("Archive file name is unterminated.");
            string path = ((record.Folder.Length == 0 ? "" : record.Folder + "/") + Encoding.Latin1.GetString(names, nameAt, zero - nameAt)).Replace('\\', '/').ToLowerInvariant();
            nameAt = zero + 1; GamePath.ValidateRelative(path);
            if (!paths.Add(path)) throw new InvalidDataException("Duplicate archive member: " + path);
            bool compressed = ((flags & 4) != 0) ^ ((record.RawSize & 0x40000000) != 0);
            long stored = record.RawSize & 0x3fffffff, start = record.Offset, size = stored;
            string? error = bounds[record.Position];
            try
            {
                if (error is null)
                {
                    file.Position = start;
                    if ((flags & 256) != 0)
                    {
                        int length = r.ReadByte();
                        if (stored < length + 1L) throw new InvalidDataException("embedded filename exceeds member boundary");
                        file.Position += length; start += length + 1; stored -= length + 1;
                    }
                    if (compressed)
                    {
                        if (stored < 4) throw new InvalidDataException("missing decoded-size prefix");
                        size = r.ReadUInt32(); start += 4; stored -= 4;
                        if (size == 0) throw new InvalidDataException("empty compressed member");
                    }
                    else size = stored;
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException) { error = e.Message; }
            long payloadStart = start, payloadBytes = stored, decodedBytes = size;
            // SSE writers may retain raw data when compression has no benefit (also used by Mutagen).
            bool decode = compressed && !(version == 105 && stored == size);
            string? memberError = error;
            byte[] prefix = [];
            if (error is null && start > record.Offset)
            { file.Position = record.Offset; prefix = r.ReadBytes(checked((int)(start - record.Offset))); }
            result.Add(new(path, size, () => Open(archive, archiveLength, version, record, payloadStart, payloadBytes, decodedBytes, decode, memberError, path, prefix)));
        }
        if (nameAt != names.Length)
            diagnostics?.Add(new("Warning", $"Archive {System.IO.Path.GetFileName(archive)} contains {names.Length - nameAt} unused filename-table bytes inside its declared metadata region. All named members and physical payload boundaries were checked; no texture bytes were trimmed."));
        return result;
    }

    private static Stream Open(string archive, long archiveLength, uint version, Record record, long start, long stored,
        long size, bool compressed, string? error, string path, byte[] prefix)
    {
        if (error is not null) throw new InvalidDataException($"Archive member bounds: {path}: {error}. Repack or restore this archive; adjacent data will not be read.");
        var file = File.OpenRead(archive);
        try
        {
            if (file.Length != archiveLength) throw new IOException("Archive changed after indexing. Analyze again.");
            file.Position = record.Position + 8; using (var r = new BinaryReader(file, Encoding.Latin1, leaveOpen: true))
                if (r.ReadUInt32() != record.RawSize || r.ReadUInt32() != record.Offset) throw new IOException("Archive member table changed. Analyze again.");
            if (prefix.Length > 0)
            {
                file.Position = record.Offset; byte[] current = new byte[prefix.Length]; file.ReadExactly(current);
                if (!current.AsSpan().SequenceEqual(prefix)) throw new IOException("Archive member prefix changed. Analyze again.");
            }
            file.Position = start;
            if (compressed && version == 105) ValidateLz4Frame(file, stored, size);
            file.Position = start;
            var bounded = new RangeStream(file, stored);
            if (!compressed) return bounded;
            Stream decoded = version == 105 ? LZ4Stream.Decode(bounded) : new ZLibStream(bounded, CompressionMode.Decompress);
            return new DecodedStream(decoded, size, path);
        }
        catch { file.Dispose(); throw; }
    }

    private static void ValidateLz4Frame(Stream source, long length, long expected)
    {
        long end = checked(source.Position + length);
        using var r = new BinaryReader(source, Encoding.Latin1, leaveOpen: true);
        void Need(long bytes) { if (bytes < 0 || bytes > end - source.Position) throw new InvalidDataException("Archive compression bounds: truncated LZ4 frame; restore/repack the archive."); }
        Need(7); if (r.ReadUInt32() != 0x184d2204) throw new InvalidDataException("Archive compression bounds: expected an SSE LZ4 frame.");
        byte flags = r.ReadByte(), descriptor = r.ReadByte();
        int blockCode = (descriptor >> 4) & 7;
        if ((flags >> 6) != 1 || (flags & 2) != 0 || (descriptor & 143) != 0 || blockCode is < 4 or > 7)
            throw new InvalidDataException("Archive compression bounds: unsupported LZ4 descriptor.");
        if ((flags & 8) != 0) { Need(8); if (r.ReadUInt64() != (ulong)expected) throw new InvalidDataException("Archive decoded-size prefix contradicts LZ4 content size."); }
        if ((flags & 1) != 0) throw new NotSupportedException("Archive LZ4 dictionaries are not supported.");
        Need(1); r.ReadByte(); int maximumBlock = 1 << (8 + blockCode * 2);
        while (true)
        {
            Need(4); uint block = r.ReadUInt32(); if (block == 0) break;
            long bytes = block & 0x7fffffff;
            if (bytes == 0 || bytes > maximumBlock) throw new InvalidDataException("Archive compression bounds: invalid LZ4 block length.");
            Need(bytes); source.Position += bytes;
            if ((flags & 16) != 0) { Need(4); source.Position += 4; }
        }
        if ((flags & 4) != 0) { Need(4); source.Position += 4; }
        if (source.Position != end) throw new InvalidDataException("Archive compression bounds: unexplained bytes after the LZ4 frame; member boundaries will not be guessed.");
    }

    private sealed class RangeStream(Stream source, long length) : Stream
    {
        private long consumed;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get => consumed; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (consumed == length || buffer.Length == 0) return 0;
            int n = source.Read(buffer[..(int)Math.Min(buffer.Length, length - consumed)]);
            if (n == 0) throw new EndOfStreamException("Archive member is truncated."); consumed += n; return n;
        }
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
    private sealed class DecodedStream(Stream source, long expected, string path) : Stream
    {
        private long read;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => expected; public override long Position { get => read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0) return 0;
            if (read == expected)
            {
                if (source.ReadByte() != -1) throw new InvalidDataException("Archive decoded length exceeds its declared member size: " + path);
                return 0;
            }
            int n = source.Read(buffer[..(int)Math.Min(buffer.Length, expected - read)]);
            if (n == 0) throw new InvalidDataException("Archive decoded length is shorter than declared: " + path);
            read += n; return n;
        }
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}

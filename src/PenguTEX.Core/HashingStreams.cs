using System.Security.Cryptography;

namespace PenguTEX.Core;

/// <summary>
/// Hashes exactly the bytes a consumer reads, so the recorded source hash describes what was processed.
/// <see cref="Complete"/> drains whatever the consumer did not need and returns the hash of the whole source.
/// </summary>
public sealed class HashingReadStream(Stream inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public long BytesRead { get; private set; }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer); _hash.AppendData(buffer[..read]); BytesRead += read; return read;
    }
    public string Complete(CancellationToken ct = default)
    {
        byte[] rest = new byte[128 * 1024];
        while (Read(rest) > 0) ct.ThrowIfCancellationRequested();
        return Convert.ToHexString(_hash.GetHashAndReset());
    }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); _hash.Dispose(); } base.Dispose(disposing); }
}

/// <summary>Hashes output while it is written, so a generated file is not read back just to hash it.</summary>
public sealed class HashingWriteStream(Stream inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public long BytesWritten { get; private set; }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer) { inner.Write(buffer); _hash.AppendData(buffer); BytesWritten += buffer.Length; }
    public string Complete() { inner.Flush(); return Convert.ToHexString(_hash.GetHashAndReset()); }
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => BytesWritten;
    public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); _hash.Dispose(); } base.Dispose(disposing); }
}

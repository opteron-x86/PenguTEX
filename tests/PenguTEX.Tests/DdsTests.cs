using System.Buffers.Binary;
using PenguTEX.Core;

namespace PenguTEX.Tests;

public class DdsTests
{
    [Theory]
    [InlineData(71)] [InlineData(72)] [InlineData(74)] [InlineData(75)] [InlineData(77)] [InlineData(78)]
    [InlineData(80)] [InlineData(81)] [InlineData(83)] [InlineData(84)] [InlineData(98)] [InlineData(99)]
    public void ReductionRetainsExactMipBytes(int dxgi)
    {
        byte[] source = Fixture.Dds(32, 8, dxgi); using var input = new MemoryStream(source);
        var (original, header) = Dds.Inspect(input); input.Position = 0;
        using var output = new MemoryStream(); Dds.Reduce(input, output, 2, source.Length);
        var bytes = output.ToArray(); output.Position = 0; var (info, _) = Dds.Inspect(output);
        Assert.Equal(8, info.Width); Assert.Equal(2, info.Height); Assert.Equal(original.MipCount - 2, info.MipCount);
        Assert.Equal(dxgi, info.Dxgi); Assert.Equal(original.Srgb, info.Srgb); Assert.Equal(original.Signed, info.Signed);
        Assert.Equal(source.AsSpan(header.Length + (int)original.RemovedBytes(2)).ToArray(), bytes.AsSpan(info.HeaderBytes).ToArray());
    }
    [Theory]
    [InlineData("DXT1")] [InlineData("DXT2")] [InlineData("DXT3")] [InlineData("DXT4")] [InlineData("DXT5")]
    [InlineData("BC4U")] [InlineData("BC4S")] [InlineData("ATI2")] [InlineData("BC5S")]
    public void LegacyHeaderSurvives(string cc)
    {
        byte[] data = Fixture.Dds(fourCC: cc); using var input = new MemoryStream(data); using var output = new MemoryStream();
        Dds.Reduce(input, output, 1, data.Length); Assert.Equal(data[84..88], output.ToArray()[84..88]);
        output.Position = 0; Assert.Equal(8, Dds.Inspect(output).Info.Width);
    }
    [Theory] [InlineData(4, 123u)] [InlineData(76, 0u)] [InlineData(16, uint.MaxValue)] [InlineData(28, 500u)]
    public void RejectsMalformedHeaders(int at, uint value)
    {
        byte[] data = Fixture.Dds(); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
        Assert.Throws<InvalidDataException>(() => Dds.Inspect(new MemoryStream(data)));
    }
    [Theory] [InlineData(112, 0x200u)] [InlineData(112, 0x200000u)] [InlineData(140, 2u)] [InlineData(132, 4u)]
    public void ProtectsOtherSurfaces(int at, uint value)
    {
        byte[] data = Fixture.Dds(); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
        Assert.Throws<NotSupportedException>(() => Dds.Inspect(new MemoryStream(data)));
    }
    [Fact] public void RejectsTruncatedOrTrailingData()
    {
        var bytes = Fixture.Dds(); Assert.Throws<InvalidDataException>(() => Dds.Inspect(new MemoryStream(bytes[..^1])));
        Assert.Throws<InvalidDataException>(() => Dds.Inspect(new MemoryStream(bytes.Concat(new byte[1]).ToArray())));
    }
    [Fact] public void OneLevelClearsMipFlags()
    {
        byte[] data = Fixture.Dds(8, 8); using var output = new MemoryStream(); Dds.Reduce(new MemoryStream(data), output, 3, data.Length);
        byte[] bytes = output.ToArray(); Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) & 0x20000);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(108)) & 0x400008);
        output.Position = 0; Assert.Equal(1, Dds.Inspect(output).Info.MipCount);
    }
    [Fact] public void TransferObservesCancellation()
    {
        byte[] data = Fixture.Dds(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => Dds.Reduce(new MemoryStream(data), new MemoryStream(), 1, data.Length, cancel.Token));
    }
}

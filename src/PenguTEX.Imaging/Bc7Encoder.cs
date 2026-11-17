using System.Runtime.InteropServices;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace PenguTEX.Imaging;

public enum Bc7Backend { Auto, Managed, Basic, Fast }

/// <summary>One stable backend per processor/job, including its cache identity.</summary>
public sealed class Bc7Encoder
{
    private const string Library = "pengutex_bc7";
    private static readonly Lazy<int> NativeVariant = new(Probe);
    private readonly BcEncoder? managed;
    public Bc7Backend Backend { get; }
    public string Identity => Backend == Bc7Backend.Managed ? "bcn-2.3.0-balanced"
        : $"bc7e-b9438627-ispc1.32.0-{Backend.ToString().ToLowerInvariant()}-linear-alpha01-v1-{(NativeVariant.Value == 8 ? "avx2" : "sse2")}";

    /// <summary>One line for the run log; says so when the much slower managed encoder is a fallback.</summary>
    public string Summary => Backend != Bc7Backend.Managed
        ? $"BC7 encoder: native BC7E {Backend} ({(NativeVariant.Value == 8 ? "AVX2" : "SSE2")})."
        : NativeVariant.Value != 0 ? "BC7 encoder: BCnEncoder (managed), selected explicitly."
        : "Warning: the native BC7 encoder could not be loaded on this system. Using the managed BCnEncoder fallback; BC7 mip completion will be far slower.";

    public Bc7Encoder(Bc7Backend backend = Bc7Backend.Auto)
    {
        bool requireNative = backend == Bc7Backend.Auto && Environment.GetEnvironmentVariable("PENGUTEX_REQUIRE_NATIVE_BC7") == "1";
        if (!Enum.IsDefined(backend)) throw new ArgumentOutOfRangeException(nameof(backend));
        if (backend == Bc7Backend.Auto)
        {
            string? requested = Environment.GetEnvironmentVariable("PENGUTEX_BC7_ENCODER");
            backend = requested?.ToLowerInvariant() switch
            {
                null or "" or "auto" => Bc7Backend.Auto,
                "managed" => Bc7Backend.Managed,
                "basic" => Bc7Backend.Basic,
                "fast" => Bc7Backend.Fast,
                _ => throw new ArgumentException("PENGUTEX_BC7_ENCODER must be auto, managed, basic or fast.")
            };
        }
        if (backend == Bc7Backend.Auto) backend = NativeVariant.Value != 0 ? Bc7Backend.Basic : Bc7Backend.Managed;
        if (backend != Bc7Backend.Managed && NativeVariant.Value == 0)
            throw new NotSupportedException("The requested native BC7 encoder is unavailable. Build/package the native library first.");
        if (requireNative && backend == Bc7Backend.Managed)
            throw new NotSupportedException("This run requires the native BC7 encoder.");
        Backend = backend;
        if (backend == Bc7Backend.Managed)
        {
            managed = new(CompressionFormat.Bc7);
            managed.Options.IsParallel = false;
            managed.OutputOptions.GenerateMipMaps = false;
            managed.OutputOptions.Quality = CompressionQuality.Balanced;
        }
    }

    public unsafe byte[] EncodeStrip(ReadOnlySpan<byte> rgba, int width, int rows)
    {
        if (width < 1 || rows is < 1 or > 4 || (long)width * rows * 4 != rgba.Length)
            throw new ArgumentException("Expected exactly one to four rows of RGBA pixels.");
        if (managed is not null) return managed.EncodeToRawBytes(rgba, width, rows, PixelFormat.Rgba32)[0];
        byte[] output = new byte[checked((int)(((long)width + 3) / 4 * 16))];
        fixed (byte* input = rgba)
        fixed (byte* destination = output)
        {
            int status = Encode(input, (nuint)rgba.Length, (uint)width, (uint)rows, destination, (nuint)output.Length,
                Backend == Bc7Backend.Basic ? 0 : 1);
            if (status != 0) throw new InvalidDataException($"Native BC7 encoding failed ({status}).");
        }
        return output;
    }

    private static int Probe()
    {
        try
        {
            if (Abi() != 2) throw new NotSupportedException("Incompatible native BC7 encoder ABI; reinstall a matching package.");
            int variant = Isa();
            if (variant is not (4 or 8)) throw new NotSupportedException("Unsupported native BC7 instruction variant.");
            return variant;
        }
        catch (DllNotFoundException) { return 0; }
        catch (BadImageFormatException) { return 0; }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, EntryPoint = "pengutex_bc7_abi", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Abi();
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, EntryPoint = "pengutex_bc7_isa", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Isa();
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, EntryPoint = "pengutex_bc7_encode", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int Encode(byte* input, nuint inputSize, uint width, uint rows, byte* output, nuint outputSize, int quality);
}

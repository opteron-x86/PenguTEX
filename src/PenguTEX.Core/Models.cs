using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenguTEX.Core;

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Empty JSON document: " + path);
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Options)); TransientIo.Replace(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>
/// On Windows, a scanner or indexer can briefly hold a file that was just written, so replacing it fails
/// with access denied or a sharing violation. Retry for about two seconds, then report the original error.
/// </summary>
public static class TransientIo
{
    public static void Replace(string source, string destination) =>
        Retry(() => File.Move(source, destination, true), OperatingSystem.IsWindows());

    public static void Retry(Action action, bool enabled, int attempts = 10, Action<int>? wait = null)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (Exception e) when (enabled && attempt < attempts && e is UnauthorizedAccessException or IOException
                && e is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException))
            { (wait ?? Thread.Sleep)(Math.Min(20 << (attempt - 1), 320)); }
        }
    }
}

/// <summary>Index-ordered parallel work for analysis: results land by index, so output never depends on timing.</summary>
public static class BoundedParallel
{
    public static int Degree(int requested) => requested is < 0 or > 64 ? throw new ArgumentException("Analysis workers must be between 1 and 64.")
        : requested > 0 ? requested : Math.Min(Environment.ProcessorCount, 8);

    /// <param name="completed">Called with a rising count every 100 items and at the end, and the index just finished.</param>
    public static void For(int count, int degree, CancellationToken ct, Action<int> body, Action<int, int>? completed = null)
    {
        int done = 0, reported = 0; var gate = new object();
        try
        {
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Degree(degree), CancellationToken = ct }, i =>
            {
                body(i);
                int now = Interlocked.Increment(ref done);
                if (completed is null || (now % 100 != 0 && now != count)) return;
                lock (gate) { if (now > reported) { reported = now; completed(now, i); } }
            });
        }
        // Report the failure itself, as a sequential loop would.
        catch (AggregateException e) when (e.InnerExceptions.Count > 0)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.Flatten().InnerExceptions[0]).Throw(); }
    }
}

public sealed record SourceOptions(string? Folder = null, string? Instance = null, string? Profile = null,
    string? Game = null, string? Mods = null, string? Profiles = null, string? Overwrite = null,
    string? WinePrefix = null, string? Ini = null, string[]? ExcludeMods = null, string? Data = null)
{
    public string? ReferenceFolder { get; init; }
    public string? ReplaceMod { get; init; }
}
public enum Severity { Error, Warning, Info }
public sealed record Diagnostic(Severity Severity, string Message);
public sealed record Asset(string Path, string Origin, string Container, long Length, Func<Stream> Open)
{
    public int Priority { get; init; }
}
public sealed record Catalog(SourceOptions Source, IReadOnlyList<string> Roots,
    IReadOnlyList<Asset> Assets, IReadOnlyList<Diagnostic> Diagnostics)
{
    public ReferenceCatalog? Reference { get; init; }
    public MaterialCatalog? Materials { get; init; }
}
public sealed record ReferenceCatalog(string Folder, string Placement, int Priority, IReadOnlyList<Asset> Assets)
{
    public Func<CancellationToken, ReferenceCatalog>? Refresh { get; init; }
}
public sealed record JobProgress(string Phase, int Done, int Total, string Path, string Message);
// ReferenceCopy values are legacy: readable in old reports and manifests, never produced, rejected by every build.
public enum TextureOperation { MipCopy, Rebuild, CompleteMips, RepairHeader, RecoverMipRecords, ReferenceCopy }
public enum Decision { Keep, Reduce, Rebuild, Protected, Unsupported, Invalid, ReferenceCopy }
public sealed record PlanItem(string Path, string Origin, string Container, string Category, Decision Decision,
    string Reason, DdsInfo? Input, int DropMips, int TargetWidth, int TargetHeight, long OutputPayload)
{
    public TextureSemantic? Semantic { get; init; }
    public bool PreserveExistingMips { get; init; }
    public int AlphaCutoff { get; init; } = 128;
    public MaterialHint? Material { get; init; }
    public TextureOperation Operation => Input?.RecordLayout is not null && Decision == Decision.Reduce ? TextureOperation.RecoverMipRecords
        : Decision == Decision.Rebuild ? PreserveExistingMips ? TextureOperation.CompleteMips : TextureOperation.Rebuild
        : Input?.DeclaredMipCount is not null && DropMips == 0 ? TextureOperation.RepairHeader : TextureOperation.MipCopy;
    public long SavedBytes => Input is null ? 0 : Input.PayloadBytes - OutputPayload;
}
public sealed record OptimizationPlan(int Schema, SourceOptions Source, Policy Policy, DateTimeOffset Created,
    IReadOnlyList<PlanItem> Items, IReadOnlyList<Diagnostic> Diagnostics)
{
    public ReferenceReport? Comparison { get; init; }
    public string? MaterialFingerprint { get; init; }
    public int UnreadableMaterialMeshes { get; init; }
    public NativeCoverage Coverage => NativeCoverage.Assess(this);
    public int ChangedCount => Items.Count(x => x.Decision is Decision.Reduce or Decision.Rebuild);
    public long SavedPayloadBytes => Items.Sum(x => x.SavedBytes);
    public long InputPayloadBytes => Items.Sum(x => x.Input?.PayloadBytes ?? 0);
    public bool CompleteSource => Diagnostics.All(d => d.Severity != Severity.Error);
}

public interface ITextureProcessor
{
    string Identity { get; }
    void Process(Stream source, Stream target, PlanItem item, long length, CancellationToken ct);
}

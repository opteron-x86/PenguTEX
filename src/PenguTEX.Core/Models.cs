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
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Options)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public sealed record SourceOptions(string? Folder = null, string? Instance = null, string? Profile = null,
    string? Game = null, string? Mods = null, string? Profiles = null, string? Overwrite = null,
    string? WinePrefix = null, string? Ini = null, string[]? ExcludeMods = null, string? Data = null)
{
    public string? ReferenceFolder { get; init; }
    public string? ReplaceMod { get; init; }
}
public sealed record Diagnostic(string Severity, string Message);
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
// ReferenceCopy is a legacy value: readable for reports, rejected by every build.
public enum Decision { Keep, Reduce, Rebuild, Protected, Unsupported, Invalid, ReferenceCopy }
public sealed record PlanItem(string Path, string Origin, string Container, string Category, Decision Decision,
    string Reason, DdsInfo? Input, int DropMips, int TargetWidth, int TargetHeight, long OutputPayload)
{
    public TextureSemantic? Semantic { get; init; }
    public bool PreserveExistingMips { get; init; }
    public int AlphaCutoff { get; init; } = 128;
    public MaterialHint? Material { get; init; }
    public string Operation => Input?.RecordLayout is not null && Decision == Decision.Reduce ? "RecoverMipRecords" : Decision == Decision.Rebuild ? PreserveExistingMips ? "CompleteMips" : "Rebuild"
        : Input?.DeclaredMipCount is not null && DropMips == 0 ? "RepairHeader" : "MipCopy";
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
    public bool CompleteSource => Diagnostics.All(d => d.Severity != "Error");
}

public interface ITextureProcessor
{
    string Identity { get; }
    void Process(Stream source, Stream target, PlanItem item, long length, CancellationToken ct);
}

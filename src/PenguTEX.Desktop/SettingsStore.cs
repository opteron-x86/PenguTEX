using PenguTEX.Core;

namespace PenguTEX.Desktop;

public sealed record DesktopSettings
{
    public SourceOptions Source { get; init; } = new(Folder: "");
    public Policy Policy { get; init; } = Policy.Preset("balanced");
    public bool CompareReference { get; init; }
    public string Output { get; init; } = "";
    public string Cache { get; init; } = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "PenguTEX");
    public int Workers { get; init; } = 2;
}
public sealed class SettingsStore
{
    public string Path { get; }
    public SettingsStore(string? path = null) => Path = path ?? System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "PenguTEX", "settings.json");
    public DesktopSettings Load() => File.Exists(Path) ? JsonFiles.Read<DesktopSettings>(Path) : new();
    public void Save(DesktopSettings settings) => JsonFiles.Write(Path, settings);
}

using PenguTEX.Core;

namespace PenguTEX.Desktop;

public sealed record DesktopSettings
{
    public SourceOptions Source { get; init; } = new(Folder: "");
    public Policy Policy { get; init; } = Policy.Preset("balanced");
    public bool CompareReference { get; init; }
    public string Output { get; init; } = "";
    public string Cache { get; init; } = DesktopPaths.Cache;
    public int Workers { get; init; } = 2;
}
public sealed class SettingsStore
{
    public string Path { get; }
    public SettingsStore(string? path = null) => Path = path ?? DesktopPaths.Settings;
    public DesktopSettings Load() => File.Exists(Path) ? JsonFiles.Read<DesktopSettings>(Path) : new();
    public void Save(DesktopSettings settings) => JsonFiles.Write(Path, settings);
}

public static class DesktopPaths
{
    private static string LocalData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PenguTEX");
    private static string Xdg(string variable, string fallback) => Path.Combine(
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value ? value
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback), "PenguTEX");
    public static string Settings => Path.Combine(OperatingSystem.IsWindows() ? LocalData : Xdg("XDG_CONFIG_HOME", ".config"), "settings.json");
    public static string Cache => OperatingSystem.IsWindows() ? Path.Combine(LocalData, "Cache") : Xdg("XDG_CACHE_HOME", ".cache");
}

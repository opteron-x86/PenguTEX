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
    // macOS: Finder launches do not inherit shell XDG variables, so default to the Library folders; an explicit XDG
    // variable, or settings/cache already at the XDG location from an earlier build, still wins.
    private static string MacLibrary(string folder)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", folder, "PenguTEX");
    private static bool HasVariable(string variable) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 };
    public static string Settings
    {
        get
        {
            if (OperatingSystem.IsWindows()) return Path.Combine(LocalData, "settings.json");
            string xdg = Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "settings.json");
            return !OperatingSystem.IsMacOS() || HasVariable("XDG_CONFIG_HOME") || File.Exists(xdg)
                ? xdg : Path.Combine(MacLibrary("Application Support"), "settings.json");
        }
    }
    public static string Cache
    {
        get
        {
            if (OperatingSystem.IsWindows()) return Path.Combine(LocalData, "Cache");
            string xdg = Xdg("XDG_CACHE_HOME", ".cache");
            return !OperatingSystem.IsMacOS() || HasVariable("XDG_CACHE_HOME") || Directory.Exists(xdg) ? xdg : MacLibrary("Caches");
        }
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace PenguTEX.Desktop;

public static class Program
{
    public static string? SettingsPath { get; private set; }
    public static bool Smoke { get; private set; }
    [STAThread] public static int Main(string[] args)
    {
        Smoke = args.Contains("--smoke");
        int at = Array.IndexOf(args, "--settings");
        if (at >= 0 && at + 1 < args.Length) SettingsPath = args[at + 1];
        return BuildApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://PenguTEX.Desktop/"))
        { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(new SettingsStore(Program.SettingsPath)); desktop.MainWindow = window;
            if (Program.Smoke)
                window.Opened += (_, _) => DispatcherTimer.RunOnce(() => desktop.Shutdown(0), TimeSpan.FromSeconds(2));
        }
        base.OnFrameworkInitializationCompleted();
    }
}

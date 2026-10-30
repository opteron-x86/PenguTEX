using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PenguTEX.Assets;
using PenguTEX.Core;
using PenguTEX.Desktop;
using PenguTEX.Imaging;
using PenguTEX.Tests;

[assembly: AvaloniaTestApplication(typeof(PenguTEX.Desktop.Tests.TestApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace PenguTEX.Desktop.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class DesktopTests
{
    private static DesktopSettings Settings(Fixture f) => new()
    {
        Source = new(Folder: f.Dir("source")), Policy = new() { Maximum = 8, NormalMaximum = 8, MaterialMaximum = 8 },
        Output = Path.Combine(f.Root, "output"), Cache = Path.Combine(f.Root, "cache"), Workers = 1,
    };
    private static T Control<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        if (Environment.GetEnvironmentVariable("PENGUTEX_SCREENSHOTS") is not { } folder) return;
        Directory.CreateDirectory(folder); using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image); image.Save(Path.Combine(folder, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    [AvaloniaFact]
    public async Task DesktopAnalysisBuildAndFiltersAgreeWithEngine()
    {
        using var f = new Fixture(); f.File("source/armor/metal.dds", Fixture.Dds(dxgi: 71));
        f.File("source/actors/character/face.dds", Fixture.Dds(dxgi: 71));
        var settings = Settings(f); Directory.CreateDirectory(settings.Output);
        var window = new MainWindow(new SettingsStore(Path.Combine(f.Root, "settings.json")));
        window.SetSettings(settings); window.Show();
        try
        {
            Capture(window, "source"); await window.AnalyzeAsync();
            Assert.NotNull(window.CurrentPlan); Assert.Equal(2, window.CurrentPlan.Items.Count); Assert.Equal(1, window.VisibleCount);
            Assert.True(Control<Button>(window, "Build").IsEnabled);
            Assert.Equal(Planner.Analyze(CatalogLoader.Load(settings.Source), settings.Policy).SavedPayloadBytes, window.CurrentPlan.SavedPayloadBytes);
            Control<ComboBox>(window, "DecisionFilter").SelectedItem = "All"; Assert.Equal(2, window.VisibleCount);
            Control<TextBox>(window, "TextureSearch").Text = "face"; Assert.Equal(1, window.VisibleCount);
            Control<TextBox>(window, "TextureSearch").Text = ""; Capture(window, "review");
            await window.BuildAsync(); Assert.NotNull(window.LastBuild); Assert.Equal(1, window.LastBuild.Files);
            Assert.Single(JobRunner.Verify(settings.Output, TestContext.Current.CancellationToken).Files); Assert.False(window.IsWorking);
            await window.BuildAsync(); Assert.Equal(1, window.LastBuild!.Reused);
            Assert.Equal(settings.Policy with { HeightMaximum = settings.Policy.MaterialMaximum }, new SettingsStore(Path.Combine(f.Root, "settings.json")).Load().Policy with { Rules = settings.Policy.Rules });
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task FastBuildLogsFinalCountsAndKeepsCompletionStatus()
    {
        using var f = new Fixture();
        for (int i = 0; i < 3; i++) f.File($"source/texture{i}.dds", Fixture.Dds());
        var settings = Settings(f) with { Workers = 3 };
        var window = new MainWindow(new SettingsStore(Path.Combine(f.Root, "settings.json"))); window.SetSettings(settings); window.Show();
        try
        {
            await window.AnalyzeAsync();
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 3;
            Dispatcher.UIThread.RunJobs();
            for (int run = 0; run < 2; run++)
            {
                Control<TextBox>(window, "JobLog").Text = "";
                await window.BuildAsync(); Dispatcher.UIThread.RunJobs();
                Assert.StartsWith("Complete: 3 verified textures", window.Status);
                string log = Control<TextBox>(window, "JobLog").Text!;
                Assert.Contains("Build: 3 / 3", log); Assert.Contains("Verify: 0 / 3", log);
                Assert.Contains("Verify: 3 / 3", log); Assert.Contains("Publish: 1 / 1", log);
                if (run == 1) Assert.Contains("Cleanup: 1 / 1", log);
                Assert.StartsWith("Complete: 3 verified textures", log.TrimEnd().Split('\n')[^1]);
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task ReferenceReviewShowsGrowthAndFitRechecksBeforeBuilding()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); f.File("reference/a.dds", Fixture.Dds(4, 4));
        var settings = Settings(f); settings = settings with { Source = settings.Source with { ReferenceFolder = f.Dir("reference") } };
        var window = new MainWindow(new SettingsStore(Path.Combine(f.Root, "settings.json"))); window.SetSettings(settings); window.Show();
        try
        {
            await window.AnalyzeAsync(); Assert.Equal(1, window.CurrentPlan!.Comparison!.LargerCount);
            Assert.Single(Control<DataGrid>(window, "ReferenceTable").ItemsSource.Cast<object>()); Capture(window, "replacement");
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 0;
            Control<CheckBox>(window, "FitReference").IsChecked = true; Assert.Null(window.CurrentPlan);
            await window.AnalyzeAsync(); Assert.False(window.CurrentPlan!.Comparison!.HasUnresolved);
            await window.BuildAsync(); Assert.False(window.LastBuild!.Manifest.Comparison!.HasUnresolved);
            Assert.Contains("In-game", window.Status);
            f.File("reference/a.dds", Fixture.Dds(8, 8));
            await window.BuildAsync(); Assert.Contains("Analyze again", window.Status);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task InvalidOutputIsReportedBeforeSourcesAreScannedAgain()
    {
        using var f = new Fixture(); f.File("source/a.dds", Fixture.Dds()); var settings = Settings(f);
        var window = new MainWindow(new SettingsStore(Path.Combine(f.Root, "settings.json"))); window.SetSettings(settings); window.Show();
        try
        {
            await window.AnalyzeAsync(); Assert.NotNull(window.CurrentPlan);
            f.Text("output/personal.txt", "keep"); Directory.Delete(settings.Source.Folder!, true);
            await window.BuildAsync();
            Assert.Contains("Choose an empty folder", window.Status); Assert.Null(window.LastBuild);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(settings.Output, "personal.txt")));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task PolicyChangesInvalidateReviewAndMissingSourceRecovers()
    {
        using var f = new Fixture(); f.File("source/metal.dds", Fixture.Dds()); var settings = Settings(f);
        var window = new MainWindow(new SettingsStore(Path.Combine(f.Root, "settings.json"))); window.SetSettings(settings); window.Show();
        try
        {
            await window.AnalyzeAsync(); Assert.NotNull(window.CurrentPlan);
            window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 0;
            Control<TextBox>(window, "Maximum").Text = "4"; Assert.Null(window.CurrentPlan); Assert.False(Control<Button>(window, "Build").IsEnabled);
            Control<TextBox>(window, "SourcePath").Text = Path.Combine(f.Root, "missing"); await window.AnalyzeAsync();
            Assert.Contains("Failed:", window.Status); Assert.False(window.IsWorking); Assert.True(Control<Button>(window, "Analyze").IsEnabled);
            window.SetSettings(settings); await window.AnalyzeAsync(); Assert.NotNull(window.CurrentPlan);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task PreviewRendersBothMipsAndChannelViews()
    {
        using var f = new Fixture(); f.File("source/metal.dds", Fixture.Dds(dxgi: 71));
        var catalog = CatalogLoader.Load(Settings(f).Source); var item = Assert.Single(Planner.Analyze(catalog, Settings(f).Policy).Items);
        var window = new PreviewWindow(Assert.Single(catalog.Assets), item); window.Show();
        try
        {
            await window.LoadAsync(); Assert.Contains("Source 16×16", window.Status); Assert.Contains("Proposed 8×8", window.Status);
            var images = window.GetVisualDescendants().OfType<Image>().ToArray(); Assert.Equal(2, images.Length); Assert.All(images, i => Assert.NotNull(i.Source));
            window.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem = "Alpha / data";
            Capture(window, "preview");
        }
        finally { window.Close(); }
    }
    [Fact]
    public void DecoderRespectsMipDimensionsAndNeverDiscardsRgbForZeroAlpha()
    {
        using var input = new MemoryStream(Fixture.Dds(width: 32, height: 8, dxgi: 71));
        var image = TexturePreview.Decode(input, 1, ct: TestContext.Current.CancellationToken); Assert.Equal(16, image.Width); Assert.Equal(4, image.Height); Assert.Equal(256, image.Rgba.Length);
        var pixel = new PixelImage([20, 40, 60, 0], 1, 1);
        Assert.Equal(new byte[] {20, 40, 60, 255}, TexturePreview.Channel(pixel, "RGB").Rgba);
        Assert.Equal(new byte[] {0, 0, 0, 255}, TexturePreview.Channel(pixel, "Alpha / data").Rgba);
    }
    [AvaloniaFact]
    public async Task DesktopBuildsExplicitCpuPolicyAndPreview()
    {
        using var f = new Fixture(); f.File("source/test.dds", Fixture.Dds(dxgi: 77, mipCount: 1));
        var settings = Settings(f) with { Policy = new() { Rules = [new("*", 8, RebuildMips: true, Semantic: TextureSemantic.LinearData)] } };
        var window = new MainWindow(new SettingsStore(Path.Combine(f.Root, "settings.json"))); window.SetSettings(settings); window.Show();
        try
        {
            await window.AnalyzeAsync(); Assert.Equal(Decision.Rebuild, Assert.Single(window.CurrentPlan!.Items).Decision); Assert.Equal(1, window.VisibleCount);
            await window.BuildAsync(); Assert.Equal("Rebuild", Assert.Single(window.LastBuild!.Manifest.Files).Operation);
            var catalog = CatalogLoader.Load(settings.Source); var preview = new PreviewWindow(catalog.Assets[0], window.CurrentPlan.Items[0]); preview.Show();
            try { await preview.LoadAsync(); Assert.Contains("Proposed 8×8", preview.Status); } finally { preview.Close(); }
        }
        finally { window.Close(); }
    }
    [Fact]
    public void DecoderRejectsSignedFormatsAndCancelledWork()
    {
        using var signed = new MemoryStream(Fixture.Dds(dxgi: 84)); Assert.Throws<NotSupportedException>(() => TexturePreview.Decode(signed, ct: TestContext.Current.CancellationToken));
        using var normal = new MemoryStream(Fixture.Dds(dxgi: 83)); Assert.Throws<OperationCanceledException>(() => TexturePreview.Decode(normal, ct: new CancellationToken(true)));
    }
}

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PenguTEX.Assets;
using PenguTEX.Assets.Profiles;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Desktop;

public sealed class MainWindow : Window
{
    private readonly SettingsStore _store;
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Texture folder", "MO2 / Fluorine profile" }, SelectedIndex = 0 };
    private readonly ComboBox _preset = new() { ItemsSource = new[] { "Quality", "Balanced", "Compact", "Custom / saved limits" }, SelectedIndex = 1 };
    private readonly ComboBox _profiles = new() { MinWidth = 180 };
    private readonly TextBox _source = Box("SourcePath"), _profile = Box("ProfileName"), _game = Box("GamePath"), _data = Box("DataPath"),
        _mods = Box("ModsPath"), _profileRoot = Box("ProfilesPath"), _overwrite = Box("OverwritePath"), _wine = Box("WinePrefix"), _ini = Box("IniPath"), _exclude = Box("ExcludedMods");
    private readonly TextBox _output = Box("OutputPath"), _cache = Box("CachePath"), _maximum = Box("Maximum"),
        _normal = Box("NormalMaximum"), _material = Box("MaterialMaximum"), _height = Box("HeightMaximum"), _workers = Box("Workers"),
        _reference = Box("ReferencePath"), _replaceMod = Box("ReplaceMod");
    private readonly CheckBox _fitReference = new() { Name = "FitReference", Content = "Fit previous dimensions and texture payload", IsChecked = false };
    private readonly CheckBox _fitProtected = new() { Name = "FitProtectedReference", Content = "Allow this fit for protected landscapes and characters", IsChecked = false };
    private readonly CheckBox _carryReference = new() { Name = "CarryReference", Content = "Carry unresolved reference files unchanged (reported separately)", IsChecked = false };
    private readonly DataGrid _comparisonTable = new() { Name = "ReferenceTable", AutoGenerateColumns = false, IsReadOnly = true, CanUserSortColumns = true };
    private readonly TextBlock _comparisonSummary = Text("Choose a previous optimizer output to compare replacements.");
    private readonly CheckBox _large = new() { Content = "Protect landscapes and large surfaces", IsChecked = true };
    private readonly CheckBox _characters = new() { Content = "Protect characters and faces", IsChecked = true };
    private readonly TextBox _rules = new() { Name = "PathRules", AcceptsReturn = true, MinHeight = 90, Text = "[]", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _search = new() { Name = "TextureSearch", PlaceholderText = "Search path, source mod, or reason…", MinWidth = 260 };
    private readonly ComboBox _filter = new() { Name = "DecisionFilter", ItemsSource = new[] { "Changed", "All", "ReferenceCopy", "Protected", "Unsupported", "Invalid", "Keep" }, SelectedIndex = 0, Width = 150 };
    private readonly DataGrid _table = new() { Name = "TextureTable", AutoGenerateColumns = false, IsReadOnly = true, CanUserSortColumns = true, SelectionMode = DataGridSelectionMode.Single };
    private readonly TextBox _log = new() { Name = "JobLog", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = Text("Choose a source and analyze its textures.", 17), _detail = Text("Select a texture to inspect its decision."), _status = Text("Ready");
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 5 };
    private readonly Button _analyze = new() { Content = "Analyze textures", Name = "Analyze", Padding = new Thickness(20, 10) },
        _build = new() { Content = "Build candidate mod", Name = "Build", IsEnabled = false, Padding = new Thickness(20, 10) },
        _cancelButton = new() { Content = "Cancel", Name = "Cancel", IsEnabled = false },
        _preview = new() { Content = "Inspect texture", IsEnabled = false }, _export = new() { Content = "Export analysis", IsEnabled = false };
    private readonly TabControl _tabs = new(); private Control? _setup; private CancellationTokenSource? _cancel;
    private Catalog? _catalog; private OptimizationPlan? _plan; private bool _loading; private string _policyName = "Balanced";
    private DateTime _lastLog; private string _lastPhase = "";
    public bool IsWorking { get; private set; }
    public string Status => _status.Text ?? "";
    public OptimizationPlan? CurrentPlan => _plan;
    public int VisibleCount { get; private set; }
    public BuildResult? LastBuild { get; private set; }

    public MainWindow(SettingsStore store)
    {
        _store = store; Title = "PenguTEX — Texture optimizer"; Width = 1280; Height = 900; MinWidth = 980; MinHeight = 700;
        var title = new StackPanel { Spacing = 5, Children = { Text("PenguTEX", 32), Text("Inspect your textures. Choose what changes. Build a removable output mod.") } };
        _setup = Setup();
        _tabs.ItemsSource = new[] { new TabItem { Header = "1  Source & policy", Content = new ScrollViewer { Content = _setup } },
            new TabItem { Header = "2  Review textures", Content = Review() }, new TabItem { Header = "3  Replacement check", Content = ReplacementReview() },
            new TabItem { Header = "4  Run log", Content = _log } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _analyze, _build, _cancelButton, _export } };
        var footer = new StackPanel { Spacing = 10, Children = { actions, _progress, _status } };
        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 18 };
        root.Children.Add(title); Grid.SetRow(_summary, 1); root.Children.Add(_summary); Grid.SetRow(_tabs, 2); root.Children.Add(_tabs);
        Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        _analyze.Click += async (_, _) => await AnalyzeAsync(); _build.Click += async (_, _) => await BuildAsync();
        _cancelButton.Click += (_, _) => _cancel?.Cancel(); _export.Click += async (_, _) => await ExportAsync();
        _preview.Click += async (_, _) =>
        {
            if (_table.SelectedItem is PlanRow row && _catalog is not null)
            {
                var sources = row.Item.Decision == Decision.ReferenceCopy ? _catalog.Reference?.Assets : _catalog.Assets;
                if (sources?.FirstOrDefault(a => a.Path == row.Item.Path) is { } asset)
                    await new PreviewWindow(asset with { Origin = row.Item.Origin }, row.Item).ShowDialog(this);
            }
        };
        _search.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Filter(); }; _filter.SelectionChanged += (_, _) => Filter();
        _table.SelectionChanged += (_, _) =>
        {
            var item = (_table.SelectedItem as PlanRow)?.Item; _preview.IsEnabled = item?.Input is not null && !IsWorking;
            _detail.Text = item is null ? "Select a texture to inspect its decision."
                : $"{item.Path}\n{item.Origin} • {item.Category} • {item.Reason}";
        };
        foreach (var box in new[] { _source, _profile, _game, _data, _mods, _profileRoot, _overwrite, _wine, _ini, _exclude, _maximum, _normal, _material, _height, _rules, _reference, _replaceMod })
            box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) InvalidatePlan(); };
        _mode.SelectionChanged += (_, _) => InvalidatePlan();
        _large.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidatePlan(); };
        _characters.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidatePlan(); };
        _fitReference.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidatePlan(); };
        _fitProtected.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidatePlan(); };
        _carryReference.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidatePlan(); };
        _preset.SelectionChanged += (_, _) => { if (!_loading && _preset.SelectedItem is string name && name != "Custom / saved limits") SetPolicy(Policy.Preset(name)); };
        _profiles.SelectionChanged += (_, _) => { if (_profiles.SelectedItem is string name) _profile.Text = name; };
        Closing += (_, e) => { if (IsWorking) { e.Cancel = true; _cancel?.Cancel(); _status.Text = "Cancelling… close again when the job stops."; } };
        try { SetSettings(store.Load()); }
        catch (Exception e) { SetSettings(new()); _status.Text = "Could not load preferences: " + e.Message; }
    }
    private static TextBox Box(string name) => new() { Name = name, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static Control Field(string label, Control input) => new StackPanel { Spacing = 5, Children = { Text(label), input } };
    private static Grid Columns(Control left, Control right)
    { var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 18 }; grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid; }
    private Control PathField(string label, TextBox box)
    {
        var browse = new Button { Content = "Browse…" }; browse.Click += async (_, _) =>
        {
            var chosen = await StorageProvider.OpenFolderPickerAsync(new() { Title = label, AllowMultiple = false });
            if (chosen.Count > 0 && chosen[0].TryGetLocalPath() is { } path) box.Text = path;
        };
        var row = new DockPanel { LastChildFill = true }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(box);
        return Field(label, row);
    }
    private Control Setup()
    {
        var load = new Button { Content = "Load profile names" }; load.Click += (_, _) =>
        {
            try { var source = ReadSettings().Source; _profiles.ItemsSource = Mo2Instance.Open(_source.Text ?? "", new(source.Game, source.Mods, source.Profiles, source.Overwrite, source.WinePrefix)).ListProfiles(); }
            catch (Exception e) { _status.Text = e.Message; }
        };
        var profileRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { load, _profiles } };
        var import = new Button { Content = "Import policy…" }; import.Click += async (_, _) =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Import JSON policy", AllowMultiple = false });
                if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { var policy = JsonFiles.Read<Policy>(path); policy.Validate(); SetPolicy(policy); }
            }
            catch (Exception e) { _status.Text = e.Message; }
        };
        var save = new Button { Content = "Save policy…" }; save.Click += async (_, _) =>
        {
            try { var path = await SavePath("Save JSON policy", "pengutex-policy.json"); if (path is not null) JsonFiles.Write(path, ReadSettings().Policy); }
            catch (Exception e) { _status.Text = e.Message; }
        };
        var advanced = new StackPanel { Spacing = 12, Children = {
            Columns(PathField("Game folder override", _game), PathField("Original Data override (unmounted)", _data)),
            Columns(PathField("Mods override", _mods), PathField("Profiles override", _profileRoot)),
            Columns(PathField("Overwrite override", _overwrite), PathField("Wine / Proton prefix", _wine)),
            Columns(Field("INI archive list override", _ini), Field("Excluded mods (separate names with |)", _exclude)),
            Field("Path rules (JSON; later matching rules win)", _rules),
            Text("Example: [{\"Pattern\":\"textures/armor/*\",\"Maximum\":1024}]. Explicit caps can override protections.") } };
        return new StackPanel { Spacing = 16, Margin = new Thickness(0, 14, 12, 10), Children = {
            Columns(Field("Source type", _mode), Field("Preset", _preset)), PathField("Source folder or MO2 / Fluorine instance", _source),
            Columns(Field("Profile name (profile sources only)", _profile), profileRow),
            Columns(Columns(Field("Color maximum", _maximum), Field("Normal maximum", _normal)), Columns(Field("Material maximum", _material), Field("Height maximum", _height))),
            Columns(_large, _characters),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { import, save } },
            Columns(PathField("Output mod folder", _output), PathField("Reusable work cache", _cache)),
            Columns(PathField("Previous optimized output (optional comparison)", _reference), Field("Mod to replace (auto-detected for installed reference)", _replaceMod)),
            Columns(_fitReference, _fitProtected),
            _carryReference,
            Field("Workers (1–32)", _workers),
            new Expander { Header = "Locations and custom rules", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch },
            Text("Quality: color 2K, normal/material/height 1K. Balanced: color 2K, normal 1K, material/height 512. Compact: color 1K, normal/material 512, height 256. Saved custom limits are preserved."),
            Text("Reference fitting uses original inputs and may choose smaller dimensions to preserve formats while meeting the previous payload. Review all exceptions before replacing an output. Estimates are texture payload, not live VRAM. Profile scans need an unmounted original Data directory.") } };
    }
    private Control ReplacementReview()
    {
        foreach (var (label, path, width) in new[] { ("Texture", "Path", 300d), ("Assessment", "Status", 185d),
            ("Previous", "Previous", 150d), ("Proposed", "Proposed", 150d), ("Change MiB", "Change", 110d), ("Reason", "Reason", 450d) })
            _comparisonTable.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = new DataGridLength(width) });
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12, Margin = new Thickness(0, 14, 0, 0) };
        grid.Children.Add(_comparisonSummary); Grid.SetRow(_comparisonTable, 1); grid.Children.Add(_comparisonTable); return grid;
    }
    private Control Review()
    {
        foreach (var (label, path, width) in new[] { ("Texture", "Path", 380d), ("Source", "Origin", 200d), ("Decision", "Decision", 100d),
            ("Format", "Format", 90d), ("Dimensions", "Dimensions", 170d), ("Saved MiB", "Saved", 110d) })
            _table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = new DataGridLength(width) });
        var toolbar = new DockPanel(); DockPanel.SetDock(_filter, Dock.Right); toolbar.Children.Add(_filter); toolbar.Children.Add(_search);
        var bottom = new StackPanel { Spacing = 10, Children = { _detail, _preview } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(0, 14, 0, 0) };
        grid.Children.Add(toolbar); Grid.SetRow(_table, 1); grid.Children.Add(_table); Grid.SetRow(bottom, 2); grid.Children.Add(bottom); return grid;
    }
    private DesktopSettings ReadSettings()
    {
        static string? Value(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
        var policy = new Policy { Name = _policyName, Maximum = int.Parse(_maximum.Text ?? ""), NormalMaximum = int.Parse(_normal.Text ?? ""),
            MaterialMaximum = int.Parse(_material.Text ?? ""), HeightMaximum = int.Parse(_height.Text ?? ""), ProtectLargeSurfaces = _large.IsChecked == true,
            ProtectCharacters = _characters.IsChecked == true, FitReference = _fitReference.IsChecked == true,
            FitProtectedReference = _fitReference.IsChecked == true && _fitProtected.IsChecked == true,
            CarryReference = _carryReference.IsChecked == true,
            Rules = JsonSerializer.Deserialize<PathRule[]>(_rules.Text ?? "[]", JsonFiles.Options) ?? [] };
        policy.Validate();
        var source = new SourceOptions(_mode.SelectedIndex == 0 ? Value(_source) : null, _mode.SelectedIndex == 1 ? Value(_source) : null,
            Value(_profile), Value(_game), Value(_mods), Value(_profileRoot), Value(_overwrite), Value(_wine), Value(_ini), Value(_exclude)?.Split('|'), Value(_data))
        { ReferenceFolder = Value(_reference), ReplaceMod = Value(_replaceMod) };
        return new() { Source = source, Policy = policy, Output = _output.Text?.Trim() ?? "", Cache = _cache.Text?.Trim() ?? "", Workers = int.Parse(_workers.Text ?? "2") };
    }
    public void SetSettings(DesktopSettings settings)
    {
        _loading = true; var s = settings.Source; _mode.SelectedIndex = s.Instance is null ? 0 : 1; _source.Text = s.Instance ?? s.Folder;
        _profile.Text = s.Profile; _game.Text = s.Game; _data.Text = s.Data; _mods.Text = s.Mods; _profileRoot.Text = s.Profiles;
        _overwrite.Text = s.Overwrite; _wine.Text = s.WinePrefix; _ini.Text = s.Ini; _exclude.Text = s.ExcludeMods is null ? "" : string.Join('|', s.ExcludeMods);
        _reference.Text = s.ReferenceFolder; _replaceMod.Text = s.ReplaceMod;
        _output.Text = settings.Output; _cache.Text = settings.Cache; _workers.Text = settings.Workers.ToString();
        SetPolicy(settings.Policy); _loading = false; InvalidatePlan();
    }
    private void SetPolicy(Policy policy)
    {
        bool wasLoading = _loading; _loading = true; _policyName = policy.Name;
        var known = new[] { "Quality", "Balanced", "Compact" }.Select(Policy.Preset).FirstOrDefault(p => p.Name == policy.Name
            && p.Maximum == policy.Maximum && p.NormalMaximum == policy.NormalMaximum && p.MaterialMaximum == policy.MaterialMaximum
            && (p.HeightMaximum ?? p.MaterialMaximum) == (policy.HeightMaximum ?? policy.MaterialMaximum));
        _preset.SelectedItem = known?.Name ?? "Custom / saved limits"; _maximum.Text = policy.Maximum.ToString(); _normal.Text = policy.NormalMaximum.ToString();
        _material.Text = policy.MaterialMaximum.ToString(); _large.IsChecked = policy.ProtectLargeSurfaces; _characters.IsChecked = policy.ProtectCharacters;
        _height.Text = (policy.HeightMaximum ?? policy.MaterialMaximum).ToString(); _fitReference.IsChecked = policy.FitReference; _fitProtected.IsChecked = policy.FitProtectedReference;
        _carryReference.IsChecked = policy.CarryReference;
        _rules.Text = JsonSerializer.Serialize(policy.Rules, JsonFiles.Options); _loading = wasLoading; InvalidatePlan();
    }
    private void InvalidatePlan()
    {
        if (_loading) return; _plan = null; _catalog = null; LastBuild = null; _table.ItemsSource = null; VisibleCount = 0;
        _comparisonTable.ItemsSource = null; _comparisonSummary.Text = "Analyze to compare with the previous output.";
        _build.IsEnabled = false; _export.IsEnabled = false; _preview.IsEnabled = false; _summary.Text = "Source or policy changed. Analyze to review a new plan.";
    }
    private void Filter()
    {
        if (_plan is null) return; string query = _search.Text ?? "", filter = _filter.SelectedItem as string ?? "Changed";
        var rows = _plan.Items.Where(i => filter == "All" || (filter == "Changed" ? i.Decision is Decision.Reduce or Decision.Rebuild or Decision.ReferenceCopy : i.Decision.ToString() == filter))
            .Where(i => (i.Path + " " + i.Origin + " " + i.Reason).Contains(query, StringComparison.OrdinalIgnoreCase)).Select(i => new PlanRow(i)).ToArray();
        VisibleCount = rows.Length; _table.ItemsSource = rows;
    }
    public async Task AnalyzeAsync() => await RunAsync(async (settings, progress, ct) =>
    {
        var analyzed = await Task.Run(() => { var catalog = CatalogLoader.Load(settings.Source, progress, ct); return (catalog, Planner.Analyze(catalog, settings.Policy, progress, ct)); }, ct);
        (_catalog, _plan) = analyzed; LastBuild = null; Filter(); _tabs.SelectedIndex = _plan.Comparison is null ? 1 : 2;
        _summary.Text = $"{_plan.Items.Count:N0} textures • {_plan.ChangedCount:N0} changes • {_plan.SavedPayloadBytes / 1048576d:N1} MiB less texture payload";
        foreach (var diagnostic in _plan.Diagnostics) AppendLog(diagnostic.Severity + ": " + diagnostic.Message);
        if (_plan.Comparison is { } comparison)
        {
            _comparisonSummary.Text = $"Replace at: {comparison.Placement} • {comparison.LargerCount:N0} larger • {comparison.UnknownCount:N0} unassessed • {comparison.IncompleteMipCount:N0} incomplete mip chains • {comparison.CarriedCount:N0} carried unchanged\n{comparison.Assessment}\nOnly this installation priority is compared. Keep other settings fixed for the in-game test.";
            _comparisonTable.ItemsSource = comparison.Items.OrderByDescending(i => i.ChangeBytes ?? long.MaxValue).Select(i => new ReferenceRow(i)).ToArray();
            AppendLog(_comparisonSummary.Text);
        }
        _status.Text = _plan.CompleteSource ? "Analysis complete. Review changes, then build a separate output mod." : "Analysis incomplete. Resolve source errors in the log before building.";
    });
    public async Task BuildAsync() => await RunAsync(async (settings, progress, ct) =>
    {
        if (_plan is null || _catalog is null) throw new InvalidOperationException("Analyze first.");
        if (string.IsNullOrWhiteSpace(settings.Output) || string.IsNullOrWhiteSpace(settings.Cache)) throw new ArgumentException("Choose output and cache folders.");
        _status.Text = "Build: checking output folder…"; AppendLog(_status.Text);
        var previous = _plan;
        LastBuild = await Task.Run(() =>
        {
            JobRunner.ValidateOutputDirectory(settings.Output);
            var recheck = new RecheckProgress(progress);
            var catalog = CatalogLoader.Load(settings.Source, recheck, ct); var fresh = Planner.Analyze(catalog, settings.Policy, recheck, ct);
            string Signature(OptimizationPlan p) => JsonSerializer.Serialize(new { p.Items, p.Comparison }, JsonFiles.Options);
            if (Signature(fresh) != Signature(previous)) throw new IOException("Source or reference winners/metadata changed. Analyze again to review the new plan.");
            return JobRunner.Build(catalog, fresh, settings.Output, settings.Cache, settings.Workers, progress, ct, new CpuMipProcessor());
        }, ct);
        _status.Text = $"Complete: {LastBuild.Files:N0} verified textures ({LastBuild.Reused:N0} reused). Candidate: {LastBuild.Output}. "
            + (LastBuild.Manifest.Comparison?.Assessment ?? "No previous-output comparison was selected. Validate in game before replacing an existing optimizer.");
        AppendLog(_status.Text); _summary.Text = $"Candidate built • {LastBuild.Bytes / 1048576d:N1} MiB of overrides • {LastBuild.Seconds:F1} seconds • game validation pending";
    });
    private async Task RunAsync(Func<DesktopSettings, IProgress<JobProgress>, CancellationToken, Task> run)
    {
        if (IsWorking) return;
        IsWorking = true; _cancel = new(); _analyze.IsEnabled = _build.IsEnabled = _preview.IsEnabled = _export.IsEnabled = false;
        _cancelButton.IsEnabled = true; _setup!.IsEnabled = false; _progress.Value = 0;
        _lastPhase = ""; _lastLog = default; bool acceptingProgress = true;
        try
        {
            var settings = ReadSettings(); _store.Save(settings);
            var progress = new Progress<JobProgress>(p =>
            {
                if (!acceptingProgress) return;
                _status.Text = $"{p.Phase}: {p.Done:N0} / {p.Total:N0} — {(p.Path.Length == 0 ? p.Message : p.Path)}";
                _progress.Value = p.Total == 0 ? 0 : 100d * p.Done / p.Total;
                if (p.Phase != _lastPhase || p.Done == p.Total || DateTime.UtcNow - _lastLog > TimeSpan.FromSeconds(2))
                { AppendLog(_status.Text); _lastLog = DateTime.UtcNow; _lastPhase = p.Phase; }
            });
            await run(settings, progress, _cancel.Token); _progress.Value = 100;
        }
        catch (OperationCanceledException) { _status.Text = "Cancelled. Previous output is preserved; verified cache can be reused."; AppendLog(_status.Text); }
        catch (Exception e) { _status.Text = "Failed: " + e.GetBaseException().Message; AppendLog(_status.Text); }
        finally
        {
            acceptingProgress = false;
            IsWorking = false; _setup.IsEnabled = true; _cancelButton.IsEnabled = false; _analyze.IsEnabled = true;
            _build.IsEnabled = _plan?.CompleteSource == true; _export.IsEnabled = _plan is not null; _preview.IsEnabled = (_table.SelectedItem as PlanRow)?.Item.Input is not null;
            _cancel.Dispose(); _cancel = null;
        }
    }
    private sealed class RecheckProgress(IProgress<JobProgress> target) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => target.Report(value with { Phase = value.Phase switch
        { "Analyze" => "Recheck textures", "Resolve" => "Recheck sources", "Archives" => "Recheck archives", _ => value.Phase } });
    }
    private void AppendLog(string? message) { _log.Text += message + Environment.NewLine; if (_log.Text.Length > 60000) _log.Text = _log.Text[^50000..]; }
    private async Task<string?> SavePath(string title, string name)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = title, SuggestedFileName = name, DefaultExtension = "json" });
        return file?.TryGetLocalPath();
    }
    private async Task ExportAsync()
    {
        try { var plan = _plan; if (plan is null) return; var path = await SavePath("Export analysis (includes local source paths)", "pengutex-analysis.json"); if (path is not null) await Task.Run(() => JsonFiles.Write(path, plan)); }
        catch (Exception e) { _status.Text = e.Message; }
    }
    private sealed record PlanRow(PlanItem Item)
    {
        public string Path => Item.Path; public string Origin => Item.Origin; public string Decision => Item.Decision.ToString();
        public string Format => (Item.Input?.Format ?? "—") + (Item.Input?.Srgb == true ? " sRGB" : "");
        public string Dimensions => Item.Input is null ? "—" : $"{Item.Input.Width}×{Item.Input.Height} → {Item.TargetWidth}×{Item.TargetHeight}";
        public string Saved => (Item.SavedBytes / 1048576d).ToString("N2");
    }
    private sealed record ReferenceRow(ReferenceDifference Item)
    {
        public string Path => Item.Path;
        public string Status => Item.Status switch
        {
            "ReferenceRetained" => "Carried unchanged", "MissingSource" => "Missing original",
            "IncompleteMips" => "Missing mip levels",
            "UnknownSource" => "Unreadable original", "UnknownReference" => "Unreadable reference",
            "HigherPriority" => "Higher priority wins", "SamePayload" => "Same payload", "NewReduction" => "New reduction", _ => Item.Status,
        };
        public string Previous => Describe(Item.Previous);
        public string Proposed => Describe(Item.Proposed);
        public string Change => Item.ChangeBytes is not { } bytes ? "Unknown" : bytes != 0 && Math.Abs(bytes) < 10485.76
            ? (bytes > 0 ? "+<0.01" : "-<0.01") : (bytes / 1048576d).ToString("+0.00;-0.00;0");
        public string Reason => Item.Reason;
        private static string Describe(DdsInfo? info) => info is null ? "Unknown" : $"{info.Width}×{info.Height} {info.Format}\n{info.MipCount} mip levels";
    }
}

using PenguTEX.Assets;
using PenguTEX.Assets.Paths;
using PenguTEX.Assets.Profiles;
using PenguTEX.Core;

namespace PenguTEX.Tests;

public class SourceTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PortableAndSeparateInstanceLocationsResolveNativePaths(bool separateBase)
    {
        using var f = new Fixture();
        string game = f.Dir("Skyrim forêt"), instance = f.Dir("Instance forêt");
        string library = separateBase ? f.Dir("Mod library forêt") : instance;
        f.Dir("Skyrim forêt/Data");
        static string Qt(string path) => path.Replace("\\", "\\\\");
        f.Text(Path.Combine(instance, "ModOrganizer.ini"), $"[General]\ngamePath=@ByteArray({Qt(game)})\nselected_profile=Profile forêt\n[Settings]\nbase_directory={Qt(library)}\nmod_directory=%BASE_DIR%/mods\nprofiles_directory=%BASE_DIR%/profiles\n");
        f.Text(Path.Combine(library, "profiles/Profile forêt/modlist.txt"), "+Textures\n-Disabled\n");
        f.Text(Path.Combine(library, "profiles/Profile forêt/settings.ini"), "[General]\nLocalSettings=true\n");
        f.Text(Path.Combine(library, "profiles/Profile forêt/skyrim.ini"), "[Archive]\nsResourceArchiveList=\nsResourceArchiveList2=\n");
        f.File(Path.Combine(library, "mods/Textures/textures/forêt.dds"), Fixture.Dds());
        f.File(Path.Combine(library, "mods/Disabled/textures/disabled.dds"), Fixture.Dds());
        var opened = Mo2Instance.Open(instance);
        Assert.Equal("Profile forêt", Assert.Single(opened.ListProfiles()));
        var catalog = CatalogLoader.Load(new(Instance: instance));
        Assert.DoesNotContain(catalog.Diagnostics, d => d.Severity == "Error");
        Assert.Equal("Textures", Assert.Single(catalog.Assets).Origin);
    }
    [Fact] public void LinuxMo2WindowsPathsUseTheSelectedPrefix()
    {
        if (OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); var prefix = f.Dir("compatdata/pfx"); f.Dir("compatdata/pfx/dosdevices");
        f.Dir("compatdata/pfx/drive_c/Games/Skyrim/Data");
        f.Text("instance/ModOrganizer.ini", "[General]\ngamePath=C:\\\\Games\\\\Skyrim\nselected_profile=Example\n[Settings]\nbase_directory=C:\\\\Mod library\n");
        f.Text("compatdata/pfx/drive_c/Mod library/profiles/Example/modlist.txt", "+Originals\n");
        f.Text("compatdata/pfx/drive_c/Mod library/profiles/Example/settings.ini", "[General]\nLocalSettings=true\n");
        f.Text("compatdata/pfx/drive_c/Mod library/profiles/Example/skyrim.ini", "[Archive]\nsResourceArchiveList=\nsResourceArchiveList2=\n");
        f.File("compatdata/pfx/drive_c/Mod library/mods/Originals/textures/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Instance: f.Dir("instance"), WinePrefix: f.Dir("compatdata")));
        Assert.DoesNotContain(catalog.Diagnostics, d => d.Severity == "Error");
        Assert.Equal("Originals", Assert.Single(catalog.Assets).Origin);
    }
    [Fact] public void WindowsReferenceFolderAliasesCannotOverlapOriginals()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); string original = f.Dir("Original textures");
        f.File("Original textures/a.dds", Fixture.Dds());
        Assert.Throws<ArgumentException>(() => CatalogLoader.Load(new(Folder: original) { ReferenceFolder = original.ToUpperInvariant() }));
        Assert.Throws<ArgumentException>(() => CatalogLoader.Load(new(Folder: original) { ReferenceFolder = Path.Combine(original.ToUpperInvariant(), "nested") }));
    }
    [Fact] public void WindowsInstalledReferenceCaseAliasKeepsItsProfilePriority()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); var source = Setup(f);
        f.File("instance/mods/Low/textures/test.dds", Fixture.Dds());
        f.File("instance/mods/High/textures/test.dds", Fixture.Dds(8, 8));
        var catalog = CatalogLoader.Load(source with { ReferenceFolder = f.Dir("instance/mods/High").ToUpperInvariant() });
        Assert.DoesNotContain(catalog.Diagnostics, d => d.Severity == "Error");
        Assert.Equal("Low", Assert.Single(catalog.Assets).Origin);
        Assert.NotNull(catalog.Reference);
        Assert.Single(catalog.Reference.Assets);
    }
    private static SourceOptions Setup(Fixture f)
    {
        string game = f.Dir("game"); f.Dir("game/Data"); f.Dir("instance/mods");
        f.Text("instance/ModOrganizer.ini", $"[General]\ngamePath={game}\nselected_profile=Example\n");
        f.Text("instance/profiles/Example/settings.ini", "[General]\nLocalSettings=true\n");
        f.Text("instance/profiles/Example/skyrim.ini", "[Archive]\nsResourceArchiveList=\nsResourceArchiveList2=\n");
        f.Text("instance/profiles/Example/modlist.txt", "+High\n-LowDisabled\n+Low\n");
        f.Dir("instance/mods/High"); f.Dir("instance/mods/Low");
        return new(Instance: Path.Combine(f.Root, "instance"));
    }
    [Fact] public void NativeProfileResolvesHighPriorityThenOverwrite()
    {
        using var f = new Fixture(); var source = Setup(f);
        f.File("instance/mods/Low/textures/test.dds", Fixture.Dds());
        f.File("instance/mods/High/Textures/Test.dds", Fixture.Dds(32));
        f.File("instance/mods/LowDisabled/textures/test.dds", Fixture.Dds(64));
        var catalog = CatalogLoader.Load(source); Assert.DoesNotContain(catalog.Diagnostics, d => d.Severity == "Error");
        Assert.Equal("High", Assert.Single(catalog.Assets).Origin);
        f.File("instance/overwrite/textures/test.dds", Fixture.Dds(64));
        Assert.Equal("overwrite", Assert.Single(CatalogLoader.Load(source).Assets).Origin);
    }
    [Fact] public void ExcludesMarkedOptimizerOutputAndHiddenTextures()
    {
        using var f = new Fixture(); var source = Setup(f);
        f.File("instance/mods/Low/textures/test.dds", Fixture.Dds());
        f.Text("instance/mods/High/VRAMrOutput.tmp", "");
        f.File("instance/mods/High/textures/test.dds", Fixture.Dds(32));
        f.File("instance/mods/Low/textures/hidden.dds.mohidden", Fixture.Dds());
        f.File("instance/mods/Low/textures/hidden.mohidden/x.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(source); Assert.Equal("Low", Assert.Single(catalog.Assets).Origin);
        Assert.Contains(catalog.Diagnostics, d => d.Message.Contains("prior optimizer"));
    }
    [Fact] public void SameLayerCaseCollisionBlocksCompleteSource()
    {
        if (OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); f.File("input/textures/A.dds", Fixture.Dds()); f.File("input/textures/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: Path.Combine(f.Root, "input")));
        Assert.Contains(catalog.Diagnostics, d => d.Severity == "Error" && d.Message.Contains("collision"));
        Assert.False(Planner.Analyze(catalog, new()).CompleteSource);
    }
    [Theory] [InlineData("pengutex-manifest.json")] [InlineData("VRAMrOutput.tmp")]
    public void MarkedOptimizerFoldersCanOnlyBeReferences(string marker)
    {
        using var f = new Fixture(); f.File("original/textures/a.dds", Fixture.Dds());
        f.File("previous/textures/a.dds", Fixture.Dds(8, 8)); f.Text("previous/" + marker, "marker");
        string previous = f.Dir("previous");
        Assert.Throws<InvalidDataException>(() => CatalogLoader.Load(new(Folder: previous)));
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("original")) { ReferenceFolder = previous });
        Assert.Single(catalog.Assets); Assert.Single(catalog.Reference!.Assets);
        var plan = Planner.Analyze(catalog, new() { FitReference = true });
        Assert.False(plan.Comparison!.HasUnresolved);
        Assert.True(JobRunner.Build(catalog, plan, f.Dir("output"), f.Dir("cache")).Manifest.OriginalsOnly);
    }
    [Fact] public void MissingEnabledRootStopsPublicationEvenWhenMeshFingerprintWouldBeUnchanged()
    {
        using var f = new Fixture(); var source = Setup(f); f.File("instance/mods/Low/textures/test.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(source, inspectMaterials: true);
        var plan = Planner.Analyze(catalog, Policy.Preset("balanced") with { Maximum = 8 });
        Directory.Delete(f.Dir("instance/mods/High"), true);
        var error = Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, f.Dir("output"), f.Dir("cache")));
        Assert.Contains("inventory became incomplete", error.Message); Assert.Empty(Directory.EnumerateFiles(f.Dir("output")));
    }
    [Fact] public void MissingEnabledModIsExplicit()
    {
        using var f = new Fixture(); var source = Setup(f); f.Text("instance/profiles/Example/modlist.txt", "+Missing\n");
        Assert.Contains(CatalogLoader.Load(source).Diagnostics, d => d.Severity == "Error");
    }
    [Fact] public void ResolvesWineZPathsAndDoesNotInventCMapping()
    {
        if (OperatingSystem.IsWindows()) return;
        using var f = new Fixture(); Assert.Equal(f.Root, HostPath.Resolve("Z:" + f.Root.Replace('/', '\\')));
        Assert.Throws<ArgumentException>(() => HostPath.Resolve("C:\\Games"));
    }
    [Fact] public void PluginActivationControlsArchiveSelection()
    {
        var selected = SkyrimArchives.Select(Mutagen.Bethesda.GameRelease.SkyrimSE,
            ["Base.bsa", "A.bsa", "A - Textures.bsa", "B.bsa"], ["A.esp"], ["Base.bsa"]);
        Assert.Equal("Base.bsa", selected[0]); Assert.Contains("A.bsa", selected); Assert.DoesNotContain("B.bsa", selected);
    }
    [Fact] public void ArchiveMembersFollowPluginOrderAndLooseFilesWin()
    {
        using var f = new Fixture(); var options = Setup(f);
        f.Text("instance/profiles/Example/plugins.txt", "*A.esp\n*B.esp\n");
        f.Text("instance/profiles/Example/loadorder.txt", "A.esp\nB.esp\n");
        f.File("instance/mods/Low/A.bsa", Fixture.Bsa(Fixture.Dds()));
        f.File("instance/mods/High/B.bsa", Fixture.Bsa(Fixture.Dds(32)));
        var catalog = CatalogLoader.Load(options);
        Assert.DoesNotContain(catalog.Diagnostics, d => d.Severity == "Error");
        var winner = Assert.Single(catalog.Assets); Assert.Equal("B.bsa", winner.Origin);
        using (var stream = winner.Open()) Assert.Equal(32, Dds.Inspect(stream, winner.Length).Info.Width);
        f.File("instance/mods/Low/textures/archive.dds", Fixture.Dds(64));
        Assert.Equal("Low", Assert.Single(CatalogLoader.Load(options).Assets).Origin);
        File.Delete(Path.Combine(f.Root, "instance/mods/Low/textures/archive.dds"));
        f.Text("instance/profiles/Example/plugins.txt", "*A.esp\n");
        Assert.Equal("A.bsa", Assert.Single(CatalogLoader.Load(options).Assets).Origin);
    }
    [Fact] public void DetectsMergedFluorineDataWithoutRejectingOrdinaryMounts()
    {
        using var f = new Fixture(); var data = f.Dir("game data"); string escaped = data.Replace(" ", "\\040");
        Assert.True(CatalogLoader.IsFluorineMount(data, [$"100 90 0:99 / {escaped} ro - fuse mo2linux rw"]));
        Assert.True(CatalogLoader.IsFluorineMount(Path.Combine(data, "child"), [$"100 90 0:99 / {escaped} ro - fuse mo2linux rw"]));
        Assert.False(CatalogLoader.IsFluorineMount(data, [$"100 90 0:99 / {escaped} ro - ext4 /dev/sda1 rw"]));
    }
    [Fact] public void ExplicitBaseDataOverridesGameFolder()
    {
        using var f = new Fixture(); var options = Setup(f); var data = f.Dir("original-data");
        f.File("original-data/textures/base.dds", Fixture.Dds());
        Assert.Single(CatalogLoader.Load(options with { Data = data }).Assets);
    }
    [Fact] public void MissingActiveIniArchiveBlocksPublicationButOptionalPatchDoesNot()
    {
        using var f = new Fixture(); var options = Setup(f);
        f.Text("instance/profiles/Example/skyrim.ini", "[Archive]\nsResourceArchiveList=Required.bsa, Skyrim - Patch.bsa\nsResourceArchiveList2=\n");
        var catalog = CatalogLoader.Load(options);
        Assert.Contains(catalog.Diagnostics, d => d.Severity == "Error" && d.Message.Contains("Required.bsa"));
        Assert.Contains(catalog.Diagnostics, d => d.Severity == "Warning" && d.Message.Contains("Patch.bsa"));
        Assert.False(Planner.Analyze(catalog, new()).CompleteSource);
    }
}

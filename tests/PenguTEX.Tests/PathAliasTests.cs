using PenguTEX.Assets;
using PenguTEX.Assets.Profiles;
using PenguTEX.Core;

namespace PenguTEX.Tests;

/// <summary>Two spellings of one folder: case on volumes that ignore it, and typed ~ or variables.</summary>
public sealed class PathAliasTests
{
    [Fact] public void CaseProbeMatchesTheVolume()
    {
        using var f = new Fixture(); f.Dir("probe");
        Assert.Equal(Directory.Exists(Path.Combine(f.Root, "PROBE")), PathCase.IgnoresCase(Path.Combine(f.Root, "probe")));
        // A path that does not exist yet is judged by its nearest existing ancestor.
        Assert.Equal(PathCase.IgnoresCase(f.Root), PathCase.IgnoresCase(Path.Combine(f.Root, "not", "yet")));
    }

    [Fact] public void CaseAliasesShareOneOutputIdentityOnlyWhereTheVolumeFoldsCase()
    {
        using var f = new Fixture();
        string upper = Path.Combine(f.Root, "Result"), lower = Path.Combine(f.Root, "result");
        Assert.Equal(PathCase.IgnoresCase(f.Root), PathCase.Key(upper) == PathCase.Key(lower));
        Assert.NotEqual(PathCase.Key(upper), PathCase.Key(Path.Combine(f.Root, "Other")));
    }

    [Fact] public void CacheSpelledAsACaseAliasOfOutputIsRejected()
    {
        using var f = new Fixture();
        if (!PathCase.IgnoresCase(f.Root)) return;
        f.File("source/textures/a.dds", Fixture.Dds());
        var catalog = CatalogLoader.Load(new(Folder: f.Dir("source")));
        var plan = Planner.Analyze(catalog, new() { Maximum = 8 });
        var error = Assert.Throws<IOException>(() => JobRunner.Build(catalog, plan, Path.Combine(f.Root, "Result"), Path.Combine(f.Root, "result"), processor: new PenguTEX.Imaging.CpuMipProcessor()));
        Assert.Contains("separate", error.Message);
    }

    [Fact] public void TypedInstanceFolderIsExpandedLikeTheOtherLocations()
    {
        // The desktop's Load profiles passes the instance text box unchanged; ~ and variables must expand there too.
        using var f = new Fixture(); string game = f.Dir("game"); f.Dir("game/Data");
        f.Text("instance/ModOrganizer.ini", $"[General]\ngamePath={game.Replace("\\", "\\\\")}\nselected_profile=Example\n");
        f.Text("instance/profiles/Example/modlist.txt", "");
        Environment.SetEnvironmentVariable("PENGUTEX_TEST_ROOT", f.Root);
        try { Assert.Equal("Example", Assert.Single(Mo2Instance.Open("%PENGUTEX_TEST_ROOT%/instance").ListProfiles())); }
        finally { Environment.SetEnvironmentVariable("PENGUTEX_TEST_ROOT", null); }
    }
}

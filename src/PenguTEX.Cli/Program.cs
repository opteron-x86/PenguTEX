using System.Text.Json;
using PenguTEX.Assets;
using PenguTEX.Assets.Profiles;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        try { return Run(args, cancel.Token); }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled; published output was preserved. Verified cached files can be reused."); return 130; }
        catch (AggregateException e) when (e.Flatten().InnerExceptions.All(x => x is OperationCanceledException)) { return 130; }
        catch (Exception e) { Console.Error.WriteLine(e.GetBaseException().Message); return 1; }
    }

    private static int Run(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h") { Console.WriteLine(Help); return 0; }
        if (args[0] == "--version") { Console.WriteLine("PenguTEX " + JobRunner.Version); return 0; }
        if (args[0] == "inspect")
        {
            if (args.Length != 2) throw new ArgumentException("Usage: PenguTEX inspect texture.dds");
            using var stream = File.OpenRead(args[1]); Print(Dds.Inspect(stream).Info); return 0;
        }
        if (args[0] == "verify")
        {
            if (args.Length != 2) throw new ArgumentException("Usage: PenguTEX verify output-folder");
            var verified = JobRunner.Verify(args[1], ct, new ConsoleProgress()); Print(verified);
            if (!verified.OriginalsOnly) Console.Error.WriteLine("File integrity passed, but this legacy output contains reference copies. Rebuild from original textures only.");
            return verified.OriginalsOnly ? 0 : 3;
        }
        var options = Parse(args.Skip(1).ToArray());
        string? Get(string name) => options.GetValueOrDefault(name);
        if (args[0] == "preset") { Print(Policy.Preset(Get("preset") ?? "balanced")); return 0; }
        if (args[0] == "profiles")
        {
            Print(Mo2Instance.Open(Get("instance") ?? throw new ArgumentException("--instance is required."),
                new(Get("game"), Get("mods"), Get("profiles"), Get("overwrite"), Get("wine-prefix"))).ListProfiles()); return 0;
        }
        if (args[0] is not ("analyze" or "build")) throw new ArgumentException("Unknown command. Use --help.");
        var source = new SourceOptions(Get("folder"), Get("instance"), Get("profile"), Get("game"), Get("mods"),
            Get("profiles"), Get("overwrite"), Get("wine-prefix"), Get("ini"), Get("exclude-mods")?.Split('|'), Get("data"))
        { ReferenceFolder = Get("reference"), ReplaceMod = Get("replace-mod") };
        Policy policy = Get("policy") is { } json ? JsonFiles.Read<Policy>(json) : Policy.Preset(Get("preset") ?? "balanced");
        if (Get("max") is { } max) policy = policy with { Maximum = int.Parse(max), NormalMaximum = int.Parse(max), MaterialMaximum = int.Parse(max), HeightMaximum = int.Parse(max) };
        if (Get("fit-reference") is { } fit) policy = policy with { FitReference = bool.Parse(fit) };
        if (Get("fit-protected") is { } fitProtected) policy = policy with { FitProtectedReference = bool.Parse(fitProtected) };
        if (Get("complete-materials") is { } complete) policy = policy with { CompleteKnownMaterials = bool.Parse(complete) };
        if (Get("recover-mip-counts") is { } recover) policy = policy with { RecoverMipCounts = bool.Parse(recover) };
        if (Get("carry-reference") is { } carry) policy = policy with { CarryReference = bool.Parse(carry) };
        policy.Validate();
        bool requireComplete = bool.Parse(Get("require-complete") ?? "false");
        bool requireParity = bool.Parse(Get("require-parity") ?? "false");
        if (requireParity && source.ReferenceFolder is null) throw new ArgumentException("--require-parity needs --reference.");
        if (args[0] == "build") JobRunner.ValidateOutputDirectory(Get("output") ?? throw new ArgumentException("--output is required."));
        var progress = new ConsoleProgress();
        var catalog = CatalogLoader.Load(source, progress, ct, policy.CompleteKnownMaterials); var plan = Planner.Analyze(catalog, policy, progress, ct);
        if (Get("report") is { } report) JsonFiles.Write(report, plan);
        Console.Error.WriteLine(plan.Coverage.Assessment);
        if (plan.Comparison is { } comparison) Console.Error.WriteLine($"Replacement: {comparison.LargerCount} larger, {comparison.UnknownCount} unassessed, {comparison.IncompleteMipCount} incomplete mip chains. {comparison.Assessment}");
        if (args[0] == "analyze") { Print(plan); return !plan.CompleteSource ? 2 : requireComplete && !plan.Coverage.MeetsPolicy ? 3 : requireParity && plan.Comparison!.HasUnresolved ? 3 : 0; }
        if (requireComplete && !plan.Coverage.MeetsPolicy) { Print(plan.Coverage); return 3; }
        if (requireParity && plan.Comparison!.HasUnresolved) { Print(plan.Comparison); return 3; }
        var result = JobRunner.Build(catalog, plan,
            Get("output") ?? throw new ArgumentException("--output is required."),
            Get("cache") ?? throw new ArgumentException("--cache is required."),
            int.Parse(Get("workers") ?? "2"), progress, ct, new CpuMipProcessor());
        Print(result); return 0;
    }
    private static Dictionary<string, string> Parse(string[] args)
    {
        string[] known = ["folder", "instance", "profile", "game", "mods", "profiles", "overwrite", "wine-prefix",
            "ini", "exclude-mods", "preset", "policy", "max", "report", "output", "cache", "workers", "data",
            "reference", "replace-mod", "fit-reference", "fit-protected", "carry-reference", "require-parity", "recover-mip-counts", "complete-materials", "require-complete"];
        var options = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--") || !known.Contains(args[i][2..]) || i + 1 == args.Length)
                throw new ArgumentException("Unknown option or missing value: " + args[i]);
            if (!options.TryAdd(args[i][2..], args[i + 1])) throw new ArgumentException("Duplicate option: " + args[i]);
        }
        return options;
    }
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonFiles.Options));
    private sealed class ConsoleProgress : IProgress<JobProgress>
    {
        private DateTime _last; private string _phase = "";
        public void Report(JobProgress p)
        {
            if (p.Phase == _phase && p.Done != p.Total && DateTime.UtcNow - _last < TimeSpan.FromSeconds(2)) return;
            _last = DateTime.UtcNow; _phase = p.Phase;
            Console.Error.WriteLine($"{p.Phase} {p.Done}/{p.Total}: {(p.Path.Length == 0 ? p.Message : p.Path)}");
        }
    }
    private const string Help = """
        PenguTEX — native Skyrim texture optimization

        --recover-mip-counts true           Recover a complete BC chain with an underreported count

        inspect texture.dds                 Validate a DDS header and payload size
        profiles --instance PATH            List MO2/Fluorine profiles
        preset --preset balanced            Print a JSON policy (quality/balanced/compact)
        analyze --folder PATH [options]     Analyze a mod/Data/textures folder
        analyze --instance PATH --profile NAME [options]
        build <source options> --output PATH --cache PATH [options]
        verify output-folder                Verify a published manifest and every texture

        Options: --preset NAME, --policy FILE, --max N, --report FILE, --workers N
        Fresh runs: --complete-materials true|false, --require-complete true|false.
        Comparison: --reference PATH, --replace-mod NAME (profile slot when needed),
                    --require-parity true
        Profile overrides: --game PATH, --mods PATH, --profiles PATH, --overwrite PATH,
                           --wine-prefix PATH, --data PATH, --ini FILE, --exclude-mods 'Mod A|Mod B'

        JSON is written to stdout; progress and errors go to stderr. analyze reads only.
        build writes a separate output mod and cache; originals remain untouched.
        Defaults protect large surfaces, character textures, generated LOD and UI.
        Explicit JSON path rules can override protections. Estimates are texture payload,
        not resident VRAM. Reference fitting preserves formats and can choose smaller
        dimensions to meet the previous payload. Install at the compared mod priority.
        Exit codes: 0 file operation succeeded, 1 failure, 2 incomplete source,
        3 required reference parity not met, 130 cancelled. Game validation is separate.
        """;
}

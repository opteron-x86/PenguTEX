using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using PenguTEX.Assets;
using PenguTEX.Assets.Archives;
using PenguTEX.Core;
using PenguTEX.Imaging;

if (args[0] == "prepare")
{
    string directory = Path.GetFullPath(args[2]); Directory.CreateDirectory(directory);
    var samples = new List<Sample>();
    void Save(string name, PixelImage image, TextureSemantic semantic, int cutoff)
    {
        string id = $"{samples.Count:D3}-{semantic}";
        File.WriteAllBytes(Path.Combine(directory, id + ".rgba"), image.Rgba);
        samples.Add(new(id, name, image.Width, image.Height, semantic, cutoff, Convert.ToHexString(SHA256.HashData(image.Rgba))));
    }
    var plan = JsonFiles.Read<OptimizationPlan>(args[1]);
    var eligible = plan.Items.Where(x => x.Decision == Decision.Rebuild && x.PreserveExistingMips && x.Input!.Format == "BC7").ToArray();
    foreach (var group in eligible.GroupBy(x => x.Semantic).OrderBy(x => x.Key))
    {
        // Spread selections across sizes, then use deterministic tiles of the first generated mip.
        var ordered = group.OrderBy(x => x.Input!.Width >> x.Input.MipCount).ThenBy(x => x.Path, StringComparer.Ordinal).ToArray();
        for (int n = 0; n < Math.Min(8, ordered.Length); n++)
        {
            var item = ordered[n * ordered.Length / Math.Min(8, ordered.Length)];
            var semantic = item.Semantic ?? throw new InvalidDataException("Missing sample semantic");
            using var stream = item.Container.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                ? File.OpenRead(item.Container)
                : BoundedBsa.Read(item.Container, new List<Diagnostic>()).Single(x => x.Path.Replace('\\', '/').Equals(item.Path, StringComparison.OrdinalIgnoreCase)).Open();
            var info = Dds.Inspect(stream, item.Input!.HeaderBytes + item.Input.PayloadBytes).Info;
            if (info != item.Input) throw new IOException("Sample metadata changed since analysis: " + item.Path);
            var image = TexturePreview.DecodePayload(stream, info, info.MipCount - 1, maximumPixels: Policy.MaximumRebuildPixels);
            double coverage = item.Semantic == TextureSemantic.OpacitySrgb ? MipFilter.Coverage(image, item.AlphaCutoff) : 0;
            int downsample = Math.Max(1, item.DropMips - (info.MipCount - 1));
            for (int level = 0; level < downsample; level++)
                image = MipFilter.Downsample(image, semantic, coverage, alphaCutoff: item.AlphaCutoff);
            int w = Math.Min(256, image.Width), h = Math.Min(256, image.Height);
            byte[] tile = new byte[w * h * 4]; int sx = (image.Width - w) / 2, sy = (image.Height - h) / 2;
            for (int y = 0; y < h; y++) image.Rgba.AsSpan(((y + sy) * image.Width + sx) * 4, w * 4).CopyTo(tile.AsSpan(y * w * 4));
            Save(item.Path, new(tile, w, h), semantic, item.AlphaCutoff);
        }
    }
    foreach (var size in new[] { (1, 1), (2, 2), (3, 7), (65, 33), (256, 256) })
    foreach (var semantic in new[] { TextureSemantic.SrgbColor, TextureSemantic.OpacitySrgb, TextureSemantic.NormalRgb, TextureSemantic.LinearData })
    {
        var (w, h) = size; byte[] pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int at = (y * w + x) * 4;
            pixels[at] = (byte)(x * 255 / Math.Max(1, w - 1)); pixels[at + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
            pixels[at + 2] = semantic == TextureSemantic.NormalRgb ? (byte)240 : (byte)((x * 13 + y * 7) % 256);
            pixels[at + 3] = semantic == TextureSemantic.SrgbColor ? (byte)255 : semantic == TextureSemantic.OpacitySrgb ? (byte)((x + y) % 3 == 0 ? 0 : 255) : (byte)((x * 17 + y * 31) % 256);
        }
        Save($"synthetic-{w}x{h}", new(pixels, w, h), semantic, 128);
    }
    JsonFiles.Write(Path.Combine(directory, "samples.json"), samples);
    Console.WriteLine($"Prepared {samples.Count} samples; {samples.Sum(x => (long)x.Width * x.Height)} pixels.");
}
else if (args[0] == "measure")
{
    string corpus = args[1], output = args[3]; Directory.CreateDirectory(output);
    int repeats = args.Length > 4 ? int.Parse(args[4]) : 3;
    var native = Marshal.GetDelegateForFunctionPointer<Encode>(NativeLibrary.GetExport(NativeLibrary.Load(Path.GetFullPath(args[2])), "encode"));
    var samples = JsonFiles.Read<Sample[]>(Path.Combine(corpus, "samples.json"));
    var results = new List<object>();
    foreach (var sample in samples)
    {
        byte[] rgba = File.ReadAllBytes(Path.Combine(corpus, sample.Id + ".rgba"));
        if (Convert.ToHexString(SHA256.HashData(rgba)) != sample.Hash) throw new IOException("Sample hash mismatch");
        for (int backend = 0; backend <= 4; backend++)
        {
            var encoder = new BcEncoder(CompressionFormat.Bc7);
            encoder.Options.IsParallel = false; encoder.OutputOptions.GenerateMipMaps = false; encoder.OutputOptions.Quality = CompressionQuality.Balanced;
            byte[] blocks = new byte[((sample.Width + 3) / 4) * ((sample.Height + 3) / 4) * 16];
            void Run()
            {
                if (backend == 0) blocks = encoder.EncodeToRawBytes(rgba, sample.Width, sample.Height, PixelFormat.Rgba32)[0];
                else if (native(backend, rgba, sample.Width, sample.Height, blocks) != 0) throw new Exception("Native encoding failed");
            }
            var warmup = Stopwatch.StartNew(); Run(); warmup.Stop();
            int batch = Math.Clamp((int)(.01 / Math.Max(warmup.Elapsed.TotalSeconds, 1e-6)), 1, 10000);
            var expected = SHA256.HashData(blocks); var times = new List<double>();
            for (int i = 0; i < repeats; i++)
            {
                var timer = Stopwatch.StartNew(); for (int n = 0; n < batch; n++) Run(); timer.Stop(); times.Add(timer.Elapsed.TotalSeconds / batch);
                if (!expected.SequenceEqual(SHA256.HashData(blocks))) throw new Exception("Nondeterministic output");
            }
            times.Sort(); double seconds = times[times.Count / 2];
            File.WriteAllBytes(Path.Combine(output, $"{sample.Id}-{backend}.bc7"), blocks);
            results.Add(new { sample.Id, Backend = backend, Seconds = seconds, Times = times, Pixels = sample.Width * sample.Height });
            Console.WriteLine($"{sample.Id} {backend}: {sample.Width * sample.Height / seconds / 1e6:F3} MP/s");
        }
        JsonFiles.Write(Path.Combine(output, "timings.json"), results);
    }
}
else if (args[0] == "snapshot")
{
    var plan = JsonFiles.Read<OptimizationPlan>(args[1]);
    var samples = JsonFiles.Read<Sample[]>(Path.Combine(args[2], "samples.json"));
    var names = samples.Select(x => x.Source).ToHashSet(StringComparer.Ordinal);
    var items = plan.Items.Where(x => names.Contains(x.Path)).ToArray();
    string directory = Path.GetFullPath(args[3]);
    if (Directory.Exists(directory)) throw new IOException("Choose a new snapshot directory");
    Directory.CreateDirectory(directory);
    foreach (var item in items)
    {
        using var source = item.Container.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
            ? File.OpenRead(item.Container)
            : BoundedBsa.Read(item.Container).Single(x => x.Path.Replace('\\', '/').Equals(item.Path, StringComparison.OrdinalIgnoreCase)).Open();
        string target = Path.Combine(directory, item.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var destination = File.Create(target); source.CopyTo(destination);
    }
    var policy = new Policy { Name = "Frozen BC7 completion benchmark", Rules = items.Select(x => new PathRule(x.Path,
        Math.Max(4, Math.Max(x.TargetWidth, x.TargetHeight)), Semantic: x.Semantic, CompleteMipChain: true, AlphaCutoff: x.AlphaCutoff)).ToArray() };
    JsonFiles.Write(Path.Combine(directory, "policy.json"), policy);
    var snapshot = Planner.Analyze(CatalogLoader.Load(new(Folder: directory)), policy);
    foreach (var original in items)
    {
        var actual = snapshot.Items.Single(x => x.Path == original.Path);
        if (actual.Decision != original.Decision || actual.DropMips != original.DropMips || actual.OutputPayload != original.OutputPayload || actual.Semantic != original.Semantic)
            throw new IOException("Snapshot did not reproduce planned completion: " + original.Path);
    }
    JsonFiles.Write(Path.Combine(directory, "plan.json"), snapshot);
    Console.WriteLine($"Snapshot contains {items.Length} original textures; planned completions match.");
}
else if (args[0] == "workflow")
{
    string directory = Path.GetFullPath(args[1]), destination = Path.GetFullPath(args[2]);
    var backend = Enum.Parse<Bc7Backend>(args[3], true); int workers = int.Parse(args[4]);
    var catalog = CatalogLoader.Load(new(Folder: directory));
    var plan = Planner.Analyze(catalog, JsonFiles.Read<Policy>(Path.Combine(directory, "policy.json")));
    string cache = Path.Combine(destination, "cache"), output = Path.Combine(destination, "output");
    if (Directory.Exists(destination)) throw new IOException("Choose a fresh workflow destination");
    var processor = new CpuMipProcessor(backend); var runs = new List<object>();
    for (int n = 0; n < 2; n++)
    {
        var timer = Stopwatch.StartNew();
        var result = JobRunner.Build(catalog, plan, output, cache, workers, processor: processor);
        var verified = JobRunner.Verify(output);
        timer.Stop();
        if (n == 0 && result.Reused != 0 || n == 1 && result.Reused != plan.ChangedCount) throw new Exception("Unexpected cache reuse");
        foreach (var item in plan.Items.Where(x => x.Decision == Decision.Rebuild))
        {
            byte[] original = File.ReadAllBytes(Path.Combine(directory, item.Path));
            byte[] produced = File.ReadAllBytes(Path.Combine(output, item.Path));
            int sourceStart = item.DropMips >= item.Input!.MipCount ? original.Length
                : checked(item.Input.HeaderBytes + (int)item.Input.RemovedBytes(item.DropMips));
            int count = Math.Max(0, original.Length - sourceStart);
            if (!original.AsSpan(sourceStart, count).SequenceEqual(produced.AsSpan(item.Input.HeaderBytes, count)))
                throw new Exception("Retained mip changed: " + item.Path);
        }
        runs.Add(new { WarmCache = n == 1, Seconds = timer.Elapsed.TotalSeconds, result.Reused, Files = verified.Files.Count,
            Processor = processor.Identity, Workers = workers, PeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64 });
        JsonFiles.Write(Path.Combine(destination, "workflow.json"), runs);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(runs[^1]));
    }
}
else throw new ArgumentException("prepare PLAN CORPUS | measure CORPUS NATIVE_LIBRARY OUTPUT [REPEATS] | snapshot PLAN CORPUS DIRECTORY | workflow SNAPSHOT OUTPUT BACKEND WORKERS");

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Encode(int backend, byte[] rgba, int width, int height, [Out] byte[] output);
record Sample(string Id, string Source, int Width, int Height, TextureSemantic Semantic, int Cutoff, string Hash);

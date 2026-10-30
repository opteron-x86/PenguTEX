using System.Security.Cryptography;
using System.Text;
using PenguTEX.Assets.Paths;
using PenguTEX.Core;

namespace PenguTEX.Assets.Materials;

public static class MaterialReader
{
    public const int MaximumMeshBytes = 64 * 1024 * 1024;
    public static MaterialCatalog Read(IReadOnlyList<Asset> meshes, List<Diagnostic> diagnostics,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var uses = new Dictionary<string, List<MaterialUse>>(StringComparer.Ordinal);
        using var signature = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int done = 0, failed = 0;
        foreach (var mesh in meshes.OrderBy(m => m.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            byte[]? bytes = null;
            try
            {
                if (mesh.Length < 0 || mesh.Length > MaximumMeshBytes) throw new InvalidDataException("Material inspection limit is 64 MiB per mesh.");
                using var source = mesh.Open(); bytes = new byte[checked((int)mesh.Length)];
                int at = 0;
                while (at < bytes.Length) { ct.ThrowIfCancellationRequested(); int n = source.Read(bytes, at, Math.Min(65536, bytes.Length - at)); if (n == 0) throw new EndOfStreamException("Truncated mesh."); at += n; }
                if (source.ReadByte() != -1) throw new InvalidDataException("Mesh length changed during inspection.");
                AddSignature(mesh.Path + "\0" + mesh.Container + "\0" + Convert.ToHexString(SHA256.HashData(bytes)));
                foreach (var (path, use) in ReadBindings(bytes, mesh.Path, mesh.Container))
                {
                    if (!uses.TryGetValue(path, out var list)) uses[path] = list = [];
                    list.Add(use);
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException)
            {
                if (bytes is not null)
                {
                    // Retain paths from readable sets as unknown when another block is malformed.
                    // This prevents a second mesh's valid binding from masking the failed use.
                    foreach (var path in UnknownPaths(bytes))
                    {
                        if (!uses.TryGetValue(path, out var list)) uses[path] = list = [];
                        list.Add(new(mesh.Path, mesh.Container, -1, -1, -1, MaterialRole.Unknown, 128, "Material metadata is malformed or unsupported."));
                    }
                }
                failed++; AddSignature(mesh.Path + "\0" + mesh.Container + "\0unreadable:" + e.Message);
                diagnostics.Add(new("Warning", $"Material inspection unavailable for {mesh.Path} ({mesh.Origin}): {e.Message}"));
            }
            if (++done % 100 == 0 || done == meshes.Count) progress?.Report(new("Materials", done, meshes.Count, mesh.Path, "Resolving winning mesh bindings"));
        }
        return new(Convert.ToHexString(signature.GetHashAndReset()), uses.ToDictionary(k => k.Key, k => MaterialHint.Summarize(k.Value), StringComparer.Ordinal), meshes.Count, failed);
        void AddSignature(string value) => signature.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
    }

    public static IReadOnlyList<(string Path, MaterialUse Use)> ReadBindings(byte[] data, string mesh, string container)
    {
        var nif = NifFile.Read(data); var result = new List<(string, MaterialUse)>();
        var sets = new Dictionary<int, string[]>(); var used = new HashSet<int>();
        foreach (var block in nif.Blocks.Where(b => b.Type == "BSShaderTextureSet"))
        {
            var r = nif.ReaderFor(block); int count = checked((int)r.U32());
            if (count > 32) throw new InvalidDataException("Unsupported texture slot count.");
            var paths = new string[count]; for (int i = 0; i < count; i++) paths[i] = r.SizedString(); sets[block.Index] = paths;
        }
        foreach (var block in nif.Blocks)
        {
            bool bsShape = block.Type is "BSTriShape" or "BSDynamicTriShape" or "BSSubIndexTriShape" or "BSMeshLODTriShape";
            bool oldShape = block.Type is "NiTriShape" or "NiTriStrips" or "BSLODTriShape" or "BSSubIndexTriShape";
            if (!bsShape && !oldShape) continue;
            var r = nif.ReaderFor(block); ObjectNet(ref r); r.Skip(4 + 52 + 4);
            if (bsShape) r.Skip(16 + 4);
            else { r.Skip(8); int materials = checked((int)r.U32()); r.Skip(checked(materials * 8)); r.Skip(5); }
            int shaderId = r.I32(), alphaId = r.I32();
            if (shaderId < 0) continue;
            var shader = Block(shaderId); var s = nif.ReaderFor(shader);
            if (shader.Type != "BSLightingShaderProperty") continue;
            uint type = s.U32(); int controller = ObjectNet(ref s);
            uint flags1 = s.U32(), flags2 = s.U32(); s.Skip(16); int setId = s.I32();
            if (!sets.TryGetValue(setId, out var paths)) throw new InvalidDataException("Lighting shader references an invalid texture set.");
            used.Add(setId);
            // SSE/LE lighting types with ordinary color/normal slots. Special shader variants stay explicit.
            bool pbr = (flags2 & (1u << 23)) != 0;
            bool ordinary = pbr || type is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 11 or 12 or 16;
            bool uncertain = controller != -1 || (flags1 & ((1u << 4) | (1u << 5) | (1u << 14) | (1u << 23) | (1u << 24) | (1u << 25))) != 0
                || (flags2 & ((1u << 19) | (1u << 28))) != 0;
            var (color, cutoff, alphaDetail) = Alpha(alphaId, flags1);
            s.Skip(20); float shaderAlpha = s.F32();
            if (color == MaterialRole.Cutout && shaderAlpha != 1f)
            { color = MaterialRole.Unknown; alphaDetail += "; shader alpha modifies the cutoff"; }
            for (int slot = 0; slot < paths.Length; slot++)
            {
                MaterialRole role = !ordinary || uncertain ? MaterialRole.Unknown : slot switch
                {
                    0 => color,
                    1 => (flags1 & (1u << 12)) != 0 ? MaterialRole.ModelNormal : MaterialRole.Normal,
                    2 when pbr || type == 2 && (flags2 & (1u << 6)) != 0 => MaterialRole.Color,
                    3 when pbr || type is 3 or 7 => MaterialRole.Height,
                    5 when pbr || type is 1 or 11 or 16 || (flags1 & (1u << 7)) != 0 => MaterialRole.Data,
                    _ => MaterialRole.Unknown,
                };
                Add(paths[slot], block.Index, shaderId, slot, role, role == MaterialRole.Cutout ? cutoff : 128,
                    $"{(pbr ? "TruePBR" : "Lighting")} type {type}; flags {flags1:X8}/{flags2:X8}; {alphaDetail}");
            }
        }
        // An orphaned texture set or an unsupported shape is evidence of unknown use, not permission to infer from a suffix.
        foreach (var (setId, paths) in sets.Where(p => !used.Contains(p.Key)))
            for (int slot = 0; slot < paths.Length; slot++) Add(paths[slot], -1, setId, slot, MaterialRole.Unknown, 128, "Texture set has no supported shape/shader binding.");
        foreach (var block in nif.Blocks.Where(b => b.Type == "BSEffectShaderProperty"))
        {
            var r = nif.ReaderFor(block); ObjectNet(ref r); r.Skip(24);
            Add(r.SizedString(), -1, block.Index, 0, MaterialRole.Unknown, 128, "Effect shader color/alpha behavior requires an explicit rule.");
            r.Skip(44); Add(r.SizedString(), -1, block.Index, 1, MaterialRole.Unknown, 128, "Effect shader palette/data requires an explicit rule.");
        }
        return result;

        NifBlock Block(int id) => id >= 0 && id < nif.Blocks.Count ? nif.Blocks[id] : throw new InvalidDataException("Invalid NIF material block reference.");
        (MaterialRole, int, string) Alpha(int id, uint flags)
        {
            if (id == -1) return (MaterialRole.Color, 128, "No alpha property");
            var b = Block(id); if (b.Type != "NiAlphaProperty") return (MaterialRole.Unknown, 128, "Unsupported alpha property");
            var a = nif.ReaderFor(b); int controller = ObjectNet(ref a); ushort bits = a.U16(); int threshold = a.U8();
            bool blend = (bits & 1) != 0, test = (bits & 512) != 0;
            if (controller != -1 || (bits & 32768) != 0 || (test && (flags & (1u << 3)) != 0))
                return (MaterialRole.Unknown, 128, "Animated/external/vertex-dependent alpha test");
            if (blend && (((bits >> 1) & 15) != 6 || ((bits >> 5) & 15) != 7))
                return (MaterialRole.Unknown, 128, "Nonstandard alpha blend factors");
            int comparison = (bits >> 10) & 7;
            if (test && comparison is not (4 or 6)) return (MaterialRole.Unknown, 128, $"Unsupported alpha comparison {comparison}");
            return (test ? MaterialRole.Cutout : blend ? MaterialRole.Blend : MaterialRole.Color,
                test ? threshold + (comparison == 4 ? 1 : 0) : 128, $"Alpha flags {bits:X4}, threshold {threshold}");
        }
        void Add(string text, int shape, int shader, int slot, MaterialRole role, int cutoff, string detail)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "\bNOR") return;
            string path = GamePath.NormalizeTexture(text).Replace('\\', '/').ToLowerInvariant();
            if (!path.EndsWith(".dds", StringComparison.Ordinal)) return;
            result.Add((path, new(mesh, container, shape, shader, slot, role, cutoff, detail)));
        }
    }
    private static string[] UnknownPaths(byte[] data)
    {
        var paths = new List<string>();
        try
        {
            var nif = NifFile.Read(data);
            foreach (var b in nif.Blocks.Where(b => b.Type == "BSShaderTextureSet"))
            {
                var r = nif.ReaderFor(b); uint count = r.U32(); if (count > 32) continue;
                for (int i = 0; i < count; i++)
                {
                    string text = r.SizedString(); if (string.IsNullOrWhiteSpace(text) || text == "\bNOR") continue;
                    string path = GamePath.NormalizeTexture(text).Replace('\\', '/').ToLowerInvariant();
                    if (path.EndsWith(".dds", StringComparison.Ordinal)) paths.Add(path);
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException) { }
        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static int ObjectNet(ref NifSpanReader r)
    { r.I32(); int count = checked((int)r.U32()); r.Skip(checked(count * 4)); return r.I32(); }
}

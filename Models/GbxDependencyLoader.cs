using GBX.NET;
using GBX.NET.Components;

namespace TM_Item_Studio.Models;

/// <summary>
/// Connects a GBX document's external reference table to files supplied by the
/// browser. References are followed on demand by the caller's async resolver;
/// only the dependency closure of the selected item is parsed.
/// </summary>
public static class GbxDependencyLoader
{
    public sealed record ResolvedFile(string Key, byte[] Bytes);

    public sealed record Result(int LoadedCount, IReadOnlyList<string> Missing,
        IReadOnlyList<string> Failed)
    {
        public bool HasUnresolved => Missing.Count > 0 || Failed.Count > 0;
    }

    public static async Task<Result> HydrateAsync(Gbx root,
        Func<string, Task<ResolvedFile?>> resolve,
        GbxReadSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(resolve);
        var readSettings = settings ?? new GbxReadSettings { SafeSkippableChunks = true };

        var pending = new Queue<Gbx>();
        var visitedTables = new HashSet<GbxRefTable>(ReferenceEqualityComparer.Instance);
        var parsedByKey = new Dictionary<string, Gbx>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var document = pending.Dequeue();
            var table = document.RefTable;
            if (table is null || !visitedTables.Add(table)) continue;

            var externalNodes = table.ExternalNodes ?? new Dictionary<string, Func<Gbx>>();
            table.ExternalNodes = externalNodes;
            foreach (var reference in table.Files)
            {
                var referencePath = reference.FilePath;
                if (string.IsNullOrWhiteSpace(referencePath) || externalNodes.ContainsKey(referencePath)) continue;
                var isOpaqueEffect = referencePath.EndsWith(".FxSystem.Gbx", StringComparison.OrdinalIgnoreCase)
                    || referencePath.EndsWith(".FxSys.Gbx", StringComparison.OrdinalIgnoreCase);
                var isParticleModel = referencePath.EndsWith(".ParticleModel.Gbx", StringComparison.OrdinalIgnoreCase);
                // Ref tables also carry raw images and other resources. They
                // are consumed by the material/texture preview pipeline, not
                // by GBX node traversal, so never feed them to Gbx.Parse.
                if (!referencePath.EndsWith(".gbx", StringComparison.OrdinalIgnoreCase)) continue;
                // CPlugFxSystem is not typed by the bundled GBX.NET build, but
                // its header/ref-table is enough to expose nested ParticleModel
                // dependencies. Parse only that header and keep the effect body
                // opaque; never hand it to the normal body parser or writer.
                if (isOpaqueEffect)
                {
                    ResolvedFile? effectFile;
                    try { effectFile = await resolve(referencePath); }
                    catch (Exception ex) { failed.Add($"{referencePath}: {ex.Message}"); continue; }
                    if (effectFile is null) { missing.Add(referencePath); continue; }

                    var effectKey = Normalize(effectFile.Key);
                    if (effectKey.Length == 0) effectKey = Normalize(referencePath);
                    if (!parsedByKey.TryGetValue(effectKey, out var effectDocument))
                    {
                        try
                        {
                            effectDocument = Gbx.ParseHeader(new MemoryStream(effectFile.Bytes, writable: false));
                            parsedByKey.Add(effectKey, effectDocument);
                        }
                        catch (Exception ex)
                        {
                            failed.Add($"{referencePath}: opaque FX header: {ex.Message}");
                            continue;
                        }
                    }

                    var capturedEffect = effectDocument;
                    externalNodes[referencePath] = () => capturedEffect;
                    pending.Enqueue(effectDocument);
                    continue;
                }
                // Shader programs, GPU buffers, sounds and texture wrappers are
                // not scene nodes. Bitmap paths are read from Material.Gbx.
                if (new[] { ".Shader.Gbx", ".Texture.Gbx", ".TextureArray.Gbx", ".Sound.Gbx", ".SoundMulti.Gbx", ".SoundEngine.Gbx", ".RefBuffer.Gbx" }
                    .Any(suffix => referencePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) continue;

                ResolvedFile? resolved;
                try
                {
                    resolved = await resolve(referencePath);
                }
                catch (Exception ex)
                {
                    failed.Add($"{referencePath}: {ex.Message}");
                    continue;
                }
                if (resolved is null)
                {
                    missing.Add(referencePath);
                    continue;
                }

                var key = Normalize(resolved.Key);
                if (key.Length == 0) key = Normalize(referencePath);
                if (!parsedByKey.TryGetValue(key, out var dependency))
                {
                    try
                    {
                        dependency = Gbx.Parse(new MemoryStream(resolved.Bytes, writable: false), readSettings);
                        parsedByKey.Add(key, dependency);
                    }
                    catch (Exception ex)
                    {
                        // Particle models can contain a newer/unknown nested
                        // node while their header/ref-table remains useful to
                        // the Studio. Keep that dependency visible as an
                        // opaque header instead of dropping the whole carrier.
                        if (!isParticleModel)
                        {
                            failed.Add($"{referencePath}: {ex.Message}");
                            continue;
                        }
                        try
                        {
                            dependency = Gbx.ParseHeader(new MemoryStream(resolved.Bytes, writable: false));
                            parsedByKey.Add(key, dependency);
                        }
                        catch (Exception headerError)
                        {
                            failed.Add($"{referencePath}: {ex.Message}; opaque header: {headerError.Message}");
                            continue;
                        }
                    }
                }

                var captured = dependency;
                externalNodes[referencePath] = () => captured;
                pending.Enqueue(dependency);
            }
        }

        return new Result(parsedByKey.Count, missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            failed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string Normalize(string value) => value.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
}

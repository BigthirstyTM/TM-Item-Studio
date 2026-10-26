using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GBX.NET;
using GBX.NET.Engines.GameData;
using Microsoft.JSInterop;
using TM_Item_Studio.Models;

namespace TM_Item_Studio.Pages;

public partial class Home
{
    private sealed record DebugDependency(string Kind, string Reference, string Status);
    private sealed record EffectReference(string Kind, string Reference, string Status);
    private sealed record DebugMessage(string Code, string Message);
    private List<DebugMessage> modelDiagnostics = new();
    private static readonly JsonSerializerOptions DebugJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Inventories the effect carrier without asking GBX.NET to parse the FX
    /// body. Official foggers/sparklers keep the FxSystem and particle models
    /// in nested RefTables; the effect body remains opaque to the typed parser,
    /// while the validated native emission fields are edited by a bounded raw
    /// splice. Keeping this inventory separate lets the Studio show the carrier
    /// without deserializing unknown particle nodes.
    /// </summary>
    private IReadOnlyList<EffectReference> EffectReferences()
    {
        var result = new List<EffectReference>();
        if (variantSources.Count == 0 || activeVariantIndex < 0 || activeVariantIndex >= variantSources.Count)
            return result;

        var activeSource = variantSources[activeVariantIndex];
        // When the body parser encounters the native CPlugFxSystem class it
        // deliberately falls back to a read-only header/asset preview. Keep
        // that class visible even though it has no typed GBX.NET node. The
        // class ID is part of the parser's stable diagnostic and avoids
        // mislabelling an unrelated unsupported model as an FX carrier.
        if (FxOpaqueExport.IsOpaqueFxCarrier(activeSource.GbxFile))
            result.Add(new("CPlugFxSystem", "(embedded CPlugFxSystem 0x0915C000)", "native-field-ready"));
        else if (activeSource.PreviewDescription?.Contains("0x0915C000", StringComparison.OrdinalIgnoreCase) == true)
            result.Add(new("CPlugFxSystem", "(unsupported embedded CPlugFxSystem 0x0915C000)", "inspection only"));

        var queue = new Queue<Gbx>();
        var visited = new HashSet<Gbx>(ReferenceEqualityComparer.Instance);
        queue.Enqueue(activeSource.Document);
        while (queue.Count > 0)
        {
            var document = queue.Dequeue();
            if (!visited.Add(document)) continue;
            var table = document.RefTable;
            if (table is null) continue;

            foreach (var file in table.Files)
            {
                var path = file.FilePath;
                if (string.IsNullOrWhiteSpace(path)) continue;
                var kind = path.EndsWith(".FxSystem.Gbx", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".FxSys.Gbx", StringComparison.OrdinalIgnoreCase)
                    ? "CPlugFxSystem"
                    : path.EndsWith(".ParticleModel.Gbx", StringComparison.OrdinalIgnoreCase)
                        ? "ParticleModel"
                        : path.Contains("Texture", StringComparison.OrdinalIgnoreCase)
                            && (path.Contains("Fogger", StringComparison.OrdinalIgnoreCase)
                                || path.Contains("Sparkler", StringComparison.OrdinalIgnoreCase)
                                || path.Contains("WaterSplash", StringComparison.OrdinalIgnoreCase))
                                ? "ParticleTexture" : null;
                if (kind is null) continue;

                var status = kind == "CPlugFxSystem"
                    ? "referenced FX / inspection only"
                    : table.ExternalNodes.ContainsKey(path) ? "geladen" : "dependency aanwezig";
                if (result.All(existing => !string.Equals(existing.Reference, path, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new(kind, path, status));
            }

            foreach (var loader in table.ExternalNodes.Values)
            {
                try
                {
                    if (loader() is { } dependency) queue.Enqueue(dependency);
                }
                catch
                {
                    // An opaque FX dependency is intentionally still visible
                    // from the parent RefTable even when it cannot be parsed.
                }
            }
        }

        return result.OrderBy(reference => reference.Kind, StringComparer.Ordinal)
            .ThenBy(reference => reference.Reference, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private (ItemScenePreview? Scene, List<DebugDependency> Dependencies, List<DebugMessage> Messages) InspectDebugScene()
    {
        var dependencies = new List<DebugDependency>();
        var messages = new List<DebugMessage>();
        if (itemModel is null) return (null, dependencies, messages);
        ItemScenePreview? preview = null;
        try
        {
            // Share the selected scene's geometry cache, but keep diagnostic state fresh.
            var scene = ReadSelectedScene();
            preview = scene.Preview;
            messages.AddRange(preview.Diagnostics.Take(200).Select(d => new DebugMessage(d.Code, d.Message)));
            // String-based legacy resource references are not GBX reference-table
            // entries. Inventory them explicitly, without resolving any files.
            void Dependency(string kind, string? reference)
            {
                if (!string.IsNullOrEmpty(reference)) dependencies.Add(new(kind, reference, "not-loaded"));
            }
            foreach (var node in scene.Handles.Sources.Values)
            {
                if (node is CGameObjectVisModel visual)
                {
                    Dependency("mesh", visual.Mesh);
                    Dependency("solid", visual.SolidRef);
                }
                if (node is CGameObjectPhyModel physical)
                {
                    Dependency("collision", physical.MoveShape);
                    Dependency("collision", physical.HitShape);
                    Dependency("trigger", physical.TriggerShape);
                }
            }
            if (gbxFile?.RefTable is { } references)
                foreach (var file in references.Files.Take(200)) Dependency("gbx-reference", file.FilePath);

            foreach (var effect in EffectReferences())
            {
                dependencies.Add(new(effect.Kind, effect.Reference, effect.Status));
                messages.Add(new("effect-reference", $"{effect.Kind}: {effect.Reference} ({effect.Status})."));
            }
        }
        catch (Exception ex)
        {
            messages.Add(new("inspection-failed", ex.Message));
        }
        if (dependencies.Count > 0)
            messages.Add(new("dependencies-not-loaded", "Referenced files are not loaded by this upload. Their presence on your disk has not been checked."));
        if (modelParts.Count == 0)
            messages.Add(new("no-mesh-geometry", "No mesh geometry available. A fallback material cannot replace an external or unsupported mesh."));
        else
            messages.Add(new("textures-not-loaded", "Neutral preview — choose a local texture folder in Materials & LOD to load matching Trackmania textures. Game materials and texture files are not read from the uploaded GBX archive."));
        return (preview, dependencies, messages);
    }

    [JSInvokable]
    public string GetDebugReportJson()
    {
        var snapshot = InspectDebugScene();
        var collection = itemModel?.Ident?.Collection;
        var report = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            status = isLoading ? "loading" : errorMessage is not null ? "error" : itemModel is null ? "empty" : "loaded",
            error = errorMessage,
            privacy = "Local report only. Contains model names, authors and referenced filenames; review before sharing. No GBX bytes, vertex buffers or texture pixels are included.",
            parser = new
            {
                informationalVersion = typeof(Gbx).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                assemblyVersion = typeof(Gbx).Assembly.GetName().Version?.ToString(),
                moduleVersionId = typeof(Gbx).Module.ModuleVersionId
            },
            selection = itemModel is null ? null : variantSources[activeVariantIndex].Name,
            views = variantSources.Select((s, i) => new { index = i, name = s.Name, active = i == activeVariantIndex }),
            identity = itemModel is null ? null : new
            {
                id = itemModel.Ident?.Id,
                author = itemModel.Ident?.Author,
                collectionNumber = collection?.Number,
                collectionString = collection?.String,
                itemType = itemModel.ItemType.ToString()
            },
            fx = !HasFxCarrier ? null : new
            {
                carrier = IsOpaqueFxCarrier ? "embedded CPlugFxSystem" : "referenced/typed",
                classId = IsOpaqueFxCarrier ? $"0x{FxOpaqueExport.FxSystemClassId:X8}" : null,
                emissionPoint = new { x = fxEmitterX, y = fxEmitterY, z = fxEmitterZ },
                emissionPointSpace = "item-local metres",
                nativeOffsetWritten = fxNativeOffsetWritten,
                nativeOffsetExportSupported = IsOpaqueFxCarrier,
                note = IsOpaqueFxCarrier
                    ? "Export writes the validated CPlugFxSystem ParticleEmitter LocalOffsetExpr fields and preserves the opaque payload outside those variable-length fields."
                    : "This report describes a referenced/typed effect carrier; native offset editing is only enabled for a validated embedded FX graph."
            },
            viewer = new
            {
                meshParts = modelParts.Count,
                vertices = modelParts.Sum(p => p.Positions.Length / 3),
                triangles = modelParts.Sum(p => p.Indices.Length / 3),
                movingParts = modelParts.Count(p => p.IsMoving),
                pivots = detectedPivots.Count, lights = detectedLights.Count,
                fxEmitterPoint = new { x = fxEmitterX, y = fxEmitterY, z = fxEmitterZ },
                fxEmitterPointSpace = "item-local metres",
                fxEmitterPointStatus = IsOpaqueFxCarrier ? "native-field-ready" : HasFxCarrier ? "preview-only" : "not-applicable",
                playing = isPlaying, materialMode = "neutral-untextured",
                note = "Viewer counts describe the displayed payload; typed scene inventory below may have different coverage."
            },
            dependencies = snapshot.Dependencies.Take(200),
            diagnostics = snapshot.Messages,
            scene = snapshot.Scene is null ? null : new
            {
                nodeCount = snapshot.Scene.Nodes.Count,
                nodes = snapshot.Scene.Nodes.Take(2000).Select(n => new { n.Path, n.ParentPath, n.Type, n.Kind, n.State }),
                materials = snapshot.Scene.Materials.Take(200),
                mappings = snapshot.Scene.Mappings.Take(500),
                collisions = snapshot.Scene.Collisions.Take(200),
                diagnostics = snapshot.Scene.Diagnostics.Take(200)
            },
            limits = new { nodes = 2000, materials = 200, mappings = 500, dependencies = 200, sceneDiagnostics = 200 },
            totals = new { dependencies = snapshot.Dependencies.Count, materials = snapshot.Scene?.Materials.Count ?? 0,
                mappings = snapshot.Scene?.Mappings.Count ?? 0, sceneDiagnostics = snapshot.Scene?.Diagnostics.Count ?? 0 }
        };
        return JsonSerializer.Serialize(report, DebugJson);
    }

    private async Task RefreshDebugDiagnostics()
    {
        modelDiagnostics = InspectDebugScene().Messages;
        await JS.InvokeVoidAsync("studioDebug.logDiagnostics", modelDiagnostics.Select(d => d.Code).Distinct());
    }

    private async Task DownloadDebugReport() => await JS.InvokeVoidAsync("studioDebug.downloadReport");
}

using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;

/// <summary>Experimental world-bound collider relay. Native handoffs can launch passengers.</summary>
public static class ItemMotionCollisionRelay
{
    public const int MaxExpandedVertices = 1_000_000;

    // Called after ItemMotionPath validates the schedule and the source binding.
    internal static ItemMotionResult<CPlugPrefab> Build(CPlugPrefab source, MotionPathPlan plan, Vector3 anchor)
    {
        ItemMotionResult<CPlugPrefab> Fail(string message) => ItemMotionResult<CPlugPrefab>.Fail(ItemMotionStatus.Unsupported, message);
        try
        {
            var seed = source.Ents.Single(e => e.Model is CPlugDynaObjectModel);
            if (seed.Rotation != Quat.Identity)
                return Fail("Experimental collision paths currently require an unrotated source body.");
            var originalBody = (CPlugDynaObjectModel)seed.Model!;
            var originalMesh = CheckMesh(originalBody);
            var vertexCount = originalMesh.Visuals!.Cast<CPlugVisualIndexedTriangles>().Sum(v => v.Vertices.Length + v.VertexStreams.Sum(s => s.Positions?.Length ?? 0));
            var collisionVertices = new[] { originalBody.StaticShape, originalBody.DynaShape }.Distinct().Sum(s => ((CPlugSurface.Mesh)s!.Surf!).Vertices.Length);
            if (((long)vertexCount + collisionVertices) * plan.Legs.Count > MaxExpandedVertices)
                return Fail($"This collision path would exceed {MaxExpandedVertices:N0} visual and collision vertices. Simplify the mesh or reduce the segment count.");
            // Independent serialized clones prevent shared geometry from being transformed twice
            // across different bodies. Nothing in the source document is mutated, even on refusal.
            var result = Clone(source);
            var bodies = new List<CPlugPrefab.EntRef>();
            var drivers = new List<CPlugPrefab.EntRef>();
            var cursor = plan.Start;
            for (var i = 0; i < plan.Legs.Count; i++)
            {
                var leg = plan.Legs[i];
                var x = Vector3.Normalize(leg.Delta);
                var cross = Vector3.Cross(x, Vector3.UnitY);
                if (cross.Length() < .05f) return Fail("Experimental collision paths cannot use vertical or near-vertical segments.");
                var z = Vector3.Normalize(cross); var y = Vector3.Cross(z, x);
                var rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1));
                var inverse = Quaternion.Inverse(rotation);
                var offset = new Vector3(0, plan.ParkingDistance, 0);
                var fresh = Clone(source);
                var body = fresh.Ents.Single(e => e.Model is CPlugDynaObjectModel);
                var model = (CPlugDynaObjectModel)body.Model!;
                var solid = CheckMesh(model);
                var seen = new HashSet<Vec3[]>();
                Vec3 Point(Vec3 p) => Pack(Vector3.Transform(V(p) - anchor, inverse) + offset);
                Vec3 Direction(Vec3 p) => Pack(Vector3.Transform(V(p), inverse));
                void Transform(Vec3[]? values, bool positions)
                {
                    if (values is null || !seen.Add(values)) return;
                    for (var j = 0; j < values.Length; j++) values[j] = positions ? Point(values[j]) : Direction(values[j]);
                }
                foreach (var visual in solid.Visuals!.Cast<CPlugVisualIndexedTriangles>().Distinct())
                {
                    for (var j = 0; j < visual.Vertices.Length; j++)
                    {
                        var v = visual.Vertices[j];
                        visual.Vertices[j] = v with { Position = Point(v.Position), Normal = v.Normal is {} n ? Direction(n) : null };
                    }
                    Transform(visual.Tangents, false); Transform(visual.BiTangents, false);
                    foreach (var stream in visual.VertexStreams.Distinct())
                    {
                        Transform(stream.Positions, true); Transform(stream.Normals, false);
                        Transform(stream.TangentUs, false); Transform(stream.TangentVs, false);
                    }
                    var positions = visual.Vertices.Select(v => V(v.Position))
                        .Concat(visual.VertexStreams.SelectMany(s => s.Positions ?? []).Select(V)).ToArray();
                    var min = positions.Aggregate(Vector3.Min); var max = positions.Aggregate(Vector3.Max);
                    var centre = (min + max) / 2; var extents = (max - min) / 2 + new Vector3(.001f);
                    visual.BoundingBox = new(centre.X, centre.Y, centre.Z, extents.X, extents.Y, extents.Z);
                }
                foreach (var shape in new[] { model.StaticShape, model.DynaShape }.Distinct())
                    Transform(((CPlugSurface.Mesh)shape!.Surf!).Vertices, true);
                model.IsStatic = false;
                body.Position = Pack(cursor - Vector3.Transform(offset, rotation));
                body.Rotation = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
                var previous = (NPlugDynaObjectModel_SInstanceParams)body.Params!;
                body.Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2, IsKinematic = true,
                    PeriodSc = 1, PeriodScMax = -1, Phase01 = 0, Phase01Max = -1,
                    TextureId = previous.TextureId, CastStaticShadow = previous.CastStaticShadow };
                bodies.Add(body);
                var end = leg.StartMs + leg.MoveMs;
                var translation = new List<KC.SubAnimFunc>();
                Add(translation, KC.AnimEase.Constant, leg.StartMs);
                Add(translation, plan.Ease, leg.MoveMs);
                Add(translation, KC.AnimEase.Constant, plan.DurationMs - end, true);
                // Outgoing body supports the waypoint pause until the next leg starts.
                var activeStart = i == 0 ? 0 : leg.StartMs;
                var activeEnd = i + 1 < plan.Legs.Count ? plan.Legs[i + 1].StartMs : plan.DurationMs;
                var fold = new List<KC.SubAnimFunc>();
                Add(fold, KC.AnimEase.Constant, activeStart, true);
                Add(fold, KC.AnimEase.Constant, activeEnd - activeStart);
                Add(fold, KC.AnimEase.Constant, plan.DurationMs - activeEnd, true);
                var driver = fresh.Ents.Single(e => e.Model is KC);
                var constraint = (KC)driver.Model!; // Preserve the source shader animation.
                constraint.TransAxis = KC.EAxis.X; constraint.TransMin = 0; constraint.TransMax = leg.Delta.Length();
                constraint.TransAnimFunc = new() { IsDuration = true, SubFuncs = translation.ToArray() };
                constraint.RotAxis = KC.EAxis.X; constraint.AngleMinDeg = 0; constraint.AngleMaxDeg = 180;
                constraint.RotAnimFunc = new() { IsDuration = true, SubFuncs = fold.ToArray() };
                driver.Position = default; driver.Rotation = Quat.Identity;
                driver.Params = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = i };
                drivers.Add(driver);
                cursor += leg.Delta;
            }
            result.Ents = [..bodies, ..drivers, ..result.Ents.Where(e => e.Model is not (KC or CPlugDynaObjectModel))];
            return ItemMotionResult<CPlugPrefab>.Ok(result);
        }
        catch (Exception e) { return Fail("Cannot build this experimental collision path: " + e.Message); }
    }

    internal static CPlugSolid2Model CheckMesh(CPlugDynaObjectModel body)
    {
        if (body.Mesh is not CPlugSolid2Model solid || solid.Visuals is not { Count: > 0 } || solid.Boxes is { Length: > 0 })
            throw new NotSupportedException("A decoded static triangle mesh without skeletal boxes is required.");
        foreach (var shape in new[] { body.StaticShape, body.DynaShape })
            if (shape?.Surf is not CPlugSurface.Mesh { Version: 6 or 7, Vertices.Length: > 0, Triangles.Length: > 0 } m
                || m.Vertices.Any(p => !Finite(p)) || m.CookedTriangles is { Length: > 0 }
                || m.OctreeCells is { Length: > 0 } || m.AABBTreeCells is { Length: > 0 }
                || m.Triangles.Any(t => t.Indices.X < 0 || t.Indices.Y < 0 || t.Indices.Z < 0 || t.Indices.X >= m.Vertices.Length || t.Indices.Y >= m.Vertices.Length || t.Indices.Z >= m.Vertices.Length))
                throw new NotSupportedException("Both collision shapes must be finite version-6/7 triangle meshes; cached or compound shapes are preserved unchanged.");
        foreach (var raw in solid.Visuals)
        {
            if (raw is not CPlugVisualIndexedTriangles v || v.SkinData is not null || v.MorphCount != 0
                || v.SubVisuals is { Length: > 0 } || v.Splits is { Length: > 0 } || v.BlendShapes is not null)
                throw new NotSupportedException("Skinned, animated, split or non-triangle visuals are not supported by collision relay.");
            if (v.Vertices.Length > 0 && v.VertexStreams.Any(s => s.Positions is { Length: > 0 }))
                throw new NotSupportedException("Mixed inline and stream positions are not supported.");
            if (v.VertexStreams.Count(s => s.Positions is { Length: > 0 }) > 1)
                throw new NotSupportedException("Multiple position streams are not supported.");
            foreach (var s in v.VertexStreams)
                if (s.StreamModel is not null || s.Positions2 is not null || s.Normals2 is not null
                    || s.TangentUs2 is not null || s.TangentVs2 is not null || s.BlendWeight is not null || s.BlendIndices is not null)
                    throw new NotSupportedException("External, blended or secondary vertex streams are not supported.");
            var positions = v.Vertices.Select(p => p.Position).Concat(v.VertexStreams.SelectMany(s => s.Positions ?? [])).ToArray();
            if (positions.Length == 0 || positions.Any(p => !Finite(p)))
                throw new NotSupportedException("Every visual needs decoded finite positions.");
        }
        return solid;
    }
    private static CPlugPrefab Clone(CPlugPrefab source)
    {
        using var stream = new MemoryStream();
        new Gbx<CPlugPrefab>(source) { BodyCompression = GbxCompression.Uncompressed }.Save(stream);
        stream.Position = 0; return Gbx.Parse<CPlugPrefab>(stream).Node;
    }
    private static void Add(List<KC.SubAnimFunc> keys, KC.AnimEase ease, int ms, bool reverse = false)
    { if (ms > 0) keys.Add(new() { Ease = ease, Duration = new TimeInt32(ms), Reverse = reverse }); }
    private static Vector3 V(Vec3 p) => new(p.X,p.Y,p.Z);
    private static Vec3 Pack(Vector3 p) => new(p.X,p.Y,p.Z);
    private static bool Finite(Vec3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}

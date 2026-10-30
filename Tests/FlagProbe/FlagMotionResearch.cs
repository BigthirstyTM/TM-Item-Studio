using System.Security.Cryptography;
using System.Text.Json;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.Plug;
using TmEssentials;
using KC = GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

// Isolated engine fixtures, not a production exporter. The installed control
// separates kinematic compatibility from the still unresolved embedding route.
internal static class FlagMotionResearch
{
    const int RotationDurationMs = 12000;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    static CPlugPrefab Prefab(CGameItemModel item) =>
        (CPlugPrefab)((NPlugItem_SVariantList)item.EntityModel!).Variants![0].EntityModel!;
    static CPlugDynaObjectModel TemplateBody(string path) =>
        (CPlugDynaObjectModel)((CPlugPrefab)Gbx.Parse<CGameItemModel>(path).Node.EntityModel!).Ents![0].Model!;
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static int Kinematic(string seed, string template, string mapSource, string output, bool phaseRange = false, bool locAnim = false, int locVersion = 2, byte rotFunc = 0, bool constraintBodyKinematic = true)
    {
        if (Directory.Exists(output)) throw new IOException("Preserve previous fixture");
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        var prefix = locAnim ? "TMIS_LocAnim_" : phaseRange ? "TMIS_KinematicRange_" : "TMIS_Kinematic_";
        if (!constraintBodyKinematic) prefix = "TMIS_ConstraintDyn_";
        var locPrefix = rotFunc == 1 ? "TMIS_LocAnim3F1_" : "TMIS_LocAnim3_";
        var names = locVersion == 3 ? new[] { locPrefix + "Visual", locPrefix + "Physical" }
            : new[] { prefix + "Stationary", prefix + "Orbit" };
        foreach (var name in names)
        {
            var doc = Gbx.Parse<CGameItemModel>(seed);
            var item = doc.Node;
            item.Name = name; item.Ident = item.Ident! with { Id = name };
            item.ArchetypeRef = "";
            var prefab = Prefab(item);
            if (prefab.Ents?.Length != 2) throw new Exception("Expected pole and cloth");
            var poleEntry = prefab.Ents[0];
            var clothEntry = prefab.Ents[1];
            var pole = (CPlugStaticObjectModel)poleEntry.Model!;
            var cloth = (CPlugDynaObjectModel)clothEntry.Model!;
            var poleBody = TemplateBody(template);
            var clothBody = TemplateBody(template);
            poleBody.Mesh = pole.Mesh; if (poleBody.References.Mesh.Descriptor is not null) poleBody.References.Mesh.Clear();
            clothBody.Mesh = cloth.Mesh; if (clothBody.References.Mesh.Descriptor is not null) clothBody.References.Mesh.Clear();
            // Both bodies share a world pivot. Bake the original cloth attachment
            // and a six-metre orbit radius into every frame and every LOD.
            Shift(poleBody.Mesh!, new(6 + poleEntry.Position.X, poleEntry.Position.Y, poleEntry.Position.Z));
            Shift(clothBody.Mesh!, new(6 + clothEntry.Position.X, clothEntry.Position.Y, clothEntry.Position.Z));
            // Complete, deliberately tiny collision surfaces keep this visual
            // compatibility probe out of the null-Havok-shape failure path.
            foreach (var body in new[] { poleBody, clothBody })
            {
                var shape = new CPlugSurface { Surf = new CPlugSurface.Mesh {
                    Version = 6, Vertices = [new(0,0,0), new(.02f,0,0), new(0,.02f,0)],
                    Triangles = [new(new Int3(0,1,2), 0, 0, 0)] } };
                shape.CreateChunk<CPlugSurface.Chunk0900C003>().Version = 2;
                body.StaticShape = shape; body.DynaShape = shape;
            }
            if (locAnim && (name.EndsWith("Orbit") || locVersion == 3)) {
                var motion = new CPlugAnimLocSimple { RotPeriod = RotationDurationMs,
                    RotPeriodMax = RotationDurationMs, TransPeriod = 0, TransPeriodMax = 0,
                    TransY = 0, Axis = 1, RotFunc = rotFunc, RotAngle = 360 };
                motion.CreateChunk<CPlugAnimLocSimple.Chunk090F8000>().Version = locVersion;
                poleBody.LocAnim = motion; clothBody.LocAnim = motion;
                poleBody.LocAnimIsPhysical = clothBody.LocAnimIsPhysical = name.EndsWith("Physical");
            }
            CPlugPrefab.EntRef Body(CPlugDynaObjectModel body) => new() {
                Position = default, Rotation = new(0,0,0,1), Model = body,
                Params = new NPlugDynaObjectModel_SInstanceParams { Version = 2,
                    PeriodSc = 8, PeriodScMax = 8, Phase01 = 0, Phase01Max = phaseRange ? 1 : 0,
                    IsKinematic = !locAnim && constraintBodyKinematic, CastStaticShadow = false, TextureId = 0 } };
            CPlugPrefab.EntRef Constraint(int slot) => new() {
                Rotation = new(0,0,0,1),
                Params = new NPlugDyna_SPrefabConstraintParams { Version = 0, Ent1 = -1, Ent2 = slot },
                Model = new KC { Version = 0, SubVersion = 3,
                    TransAxis = KC.EAxis.Y, TransMin = 0, TransMax = 0,
                    RotAxis = KC.EAxis.Y, AngleMinDeg = 0,
                    AngleMaxDeg = name.EndsWith("Orbit") ? 360 : 0,
                    TransAnimFunc = Timeline(RotationDurationMs), RotAnimFunc = Timeline(RotationDurationMs) } };
            prefab.Ents = locAnim ? [Body(poleBody), Body(clothBody)]
                : [Body(poleBody), Body(clothBody), Constraint(0), Constraint(1)];
            var path = Path.Combine(output, name + ".Item.Gbx");
            new ReferenceTableDocument(item, doc.RefTable!).Save(path);
            var parsed = Gbx.Parse<CGameItemModel>(path);
            var p = Prefab(parsed.Node);
            if (p.Ents!.Length != (locAnim ? 2 : 4) || p.Ents.Count(e => e.Model is KC) != (locAnim ? 0 : 2))
                throw new Exception("Kinematic graph did not survive serialization");
            var mesh = ((CPlugDynaObjectModel)p.Ents[1].Model!).Mesh!;
            if (mesh.Visuals!.Count != 5 || mesh.Visuals.Any(v => v.SubVisuals?.Length != 86))
                throw new Exception("Cloth animation changed");
            rows.Add(new { name, path, sha256 = Hash(path), scope = "Installed visual-motion control; tiny diagnostic collision, not vanilla collision parity" });
        }
        var map = Gbx.Parse<CGameCtnChallenge>(mapSource, new GbxReadSettings { SkipChunkIds = [0x0304305B] });
        if (map.Node.AnchoredObjects?.Count != 3) throw new Exception("Expected three-anchor seed map");
        map.Node.AnchoredObjects.RemoveAt(2);
        for (var i = 0; i < 2; i++) {
            var a = map.Node.AnchoredObjects[i];
            a.ItemModel = a.ItemModel with { Id = names[i] };
            a.AbsolutePositionInMap = new(748 + 40*i, 128, 450);
        }
        map.Node.MapName = locAnim ? "TMIS Native Local Motion Controls" : phaseRange ? "TMIS Kinematic Phase Range Controls" : "TMIS Kinematic Waving Flag Controls";
        map.Node.MapUid = locAnim ? "TMISLocalMotion20261022" : phaseRange ? "TMISKinematicRange20261022" : "TMISKinematicFlags20261022";
        if (locVersion == 3) { map.Node.MapName = "TMIS Native Local Motion V3 Controls"; map.Node.MapUid = "TMISLocalMotion3_20261022"; }
        if (rotFunc == 1) { map.Node.MapName = "TMIS Native Local Motion V3 Function1"; map.Node.MapUid = "TMISLocalMotion3F1_20261022"; }
        if (!constraintBodyKinematic) { map.Node.MapName = "TMIS Nonkinematic Constraint Controls"; map.Node.MapUid = "TMISConstraintDyn20261022"; }
        map.Node.EmbeddedZipData = null;
        var mapPath = Path.Combine(output, !constraintBodyKinematic ? "TMIS_ConstraintDyn_20261022.Map.Gbx" : rotFunc == 1 ? "TMIS_LocalMotion3F1_20261022.Map.Gbx" : locVersion == 3 ? "TMIS_LocalMotion3_20261022.Map.Gbx" : locAnim ? "TMIS_LocalMotion_20261022.Map.Gbx" : phaseRange ? "TMIS_KinematicRange_20261022.Map.Gbx" : "TMIS_KinematicFlags_20261022.Map.Gbx");
        map.Save(mapPath);
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
            models = rows, map = mapPath, mapSha256 = Hash(mapPath),
            rotationDurationMs = RotationDurationMs,
            phaseRange,
            locAnim,
            locVersion,
            rotFunc,
            constraintBodyKinematic,
            note = "Game-drive installation control only. Both bodies use identical timing. Diagnostic collisions are not vanilla parity." }, Json));
        return 0;
    }

    static KC.AnimFunc Timeline(int duration) => new() { IsDuration = true,
        SubFuncs = [new() { Ease = KC.AnimEase.Linear, Reverse = false, Duration = new TimeInt32(duration) }] };

    static void Shift(CPlugSolid2Model mesh, Vec3 d)
    {
        Vec3 Add(Vec3 p) => new(p.X+d.X,p.Y+d.Y,p.Z+d.Z);
        foreach (var v in mesh.Visuals!.OfType<CPlugVisualIndexedTriangles>()) {
            v.Vertices = v.Vertices.Select(p => p with { Position = Add(p.Position) }).ToArray();
            foreach (var s in v.VertexStreams)
                if (s.Positions is not null) s.Positions = s.Positions.Select(Add).ToArray();
            var b = v.BoundingBox;
            v.BoundingBox = new(b.X+d.X,b.Y+d.Y,b.Z+d.Z,b.X2,b.Y2,b.Z2);
        }
    }
}

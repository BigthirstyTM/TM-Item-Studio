using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Plug;
using KC=GBX.NET.Engines.Meta.NPlugDyna_SKinematicConstraint;

namespace TM_Item_Studio.Models;
public enum MotionPartKind { Wing, Wheel, Propeller }
public sealed record MotionPathPart(int VisualIndex, MotionPartKind Kind=MotionPartKind.Wing,
    Vector3 Pivot=default, KC.EAxis Axis=KC.EAxis.X, float MinimumAngle=-25, float MaximumAngle=40,
    int PeriodMs=700, float Radius=.4f, bool Reverse=false);

public static class ItemMotionParts
{
    public static string? Validate(MotionPathPlan p) {
        if(p.Parts is not {Length:>0} parts)return null;
        if(parts.Any(p=>p is null))return "Missing animated part settings.";
        if(p.Use==MotionPathUse.Obstacle || p.Rotations is null)return "Separate part animation requires scenery or experimental collision relay with route rotation.";
        if(parts.Length>8 || parts.Select(x=>x.VisualIndex).Distinct().Count()!=parts.Length)return "Choose at most eight distinct visual meshes for moving parts.";
        foreach(var part in parts)if(part.VisualIndex<0 || !Enum.IsDefined(part.Kind) || !Enum.IsDefined(part.Axis)
            || !float.IsFinite(part.Pivot.Length()) || part.Pivot.Length()>10000 || !float.IsFinite(part.MinimumAngle) || !float.IsFinite(part.MaximumAngle)
            || Math.Abs(part.MinimumAngle)>3600 || Math.Abs(part.MaximumAngle)>3600 || part.PeriodMs is <160 or >60000 || !float.IsFinite(part.Radius) || part.Radius is <.01f or >100)
            return "Invalid part: finite pivot/angles, period 160–60000 ms, wheel radius 0.01–100 m required.";
        return null;
    }
    public static CPlugDynaObjectModel Extract(CPlugDynaObjectModel source,IReadOnlySet<int> selected,Vector3 pivot) {
        using var stream=new MemoryStream();new Gbx<CPlugDynaObjectModel>(source){BodyCompression=GbxCompression.Uncompressed}.Save(stream);stream.Position=0;
        var body=Gbx.Parse<CPlugDynaObjectModel>(stream).Node;
        var solid=ItemMotionCollisionRelay.CheckMesh(body);
        var indices=Enumerable.Range(0,solid.Visuals!.Count).Where(selected.Contains).ToArray();
        var maps=solid.ShadedGeoms!.Where(m=>selected.Contains(m.VisualIndex)).ToArray();
        foreach(var m in maps)m.VisualIndex=Array.IndexOf(indices,m.VisualIndex);
        solid.ShadedGeoms=maps;solid.Visuals=[..indices.Select(i=>solid.Visuals[i])];
        var seen=new HashSet<Vec3[]>();
        Vec3 Shift(Vec3 v)=>new(v.X-pivot.X,v.Y-pivot.Y,v.Z-pivot.Z);
        foreach(var v in solid.Visuals.Cast<CPlugVisualIndexedTriangles>()) {
            for(int i=0;i<v.Vertices.Length;i++)v.Vertices[i]=v.Vertices[i] with{Position=Shift(v.Vertices[i].Position)};
            foreach(var s in v.VertexStreams)if(s.Positions is {} pos && seen.Add(pos))for(int i=0;i<pos.Length;i++)pos[i]=Shift(pos[i]);
            var box=v.BoundingBox;v.BoundingBox=new(box.X-pivot.X,box.Y-pivot.Y,box.Z-pivot.Z,box.X2,box.Y2,box.Z2);
        }
        return body;
    }
}

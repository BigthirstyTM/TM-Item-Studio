using GBX.NET;

namespace TM_Item_Studio.Models;

/// <summary>Triangulates simple crystal polygons, retaining authored corner indices and UV seams.</summary>
internal static class CrystalPolygon
{
    internal static bool TryTriangulate(IReadOnlyList<Vec3> points, out int[] triangles)
    {
        triangles = [];
        // Crystal faces normally have a byte-sized corner count. Bound malformed input work.
        if (points.Count < 3 || points.Count > 512) return false;
        if (points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))) return false;
        double nx = 0, ny = 0, nz = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            nx += ((double)a.Y - b.Y) * ((double)a.Z + b.Z);
            ny += ((double)a.Z - b.Z) * ((double)a.X + b.X);
            nz += ((double)a.X - b.X) * ((double)a.Y + b.Y);
        }
        // Project along the dominant Newell normal; translation and scale normalization
        // keep predicates stable for small objects and large authored coordinates.
        var axis = Math.Abs(nx) >= Math.Abs(ny) && Math.Abs(nx) >= Math.Abs(nz) ? 0 : Math.Abs(ny) >= Math.Abs(nz) ? 1 : 2;
        var xy = points.Select(p => axis == 0 ? ((double)p.Y, (double)p.Z)
            : axis == 1 ? ((double)p.Z, (double)p.X) : ((double)p.X, (double)p.Y)).ToArray();
        var minX = xy.Min(p => p.Item1); var minY = xy.Min(p => p.Item2);
        var scale = Math.Max(xy.Max(p => p.Item1) - minX, xy.Max(p => p.Item2) - minY);
        if (scale <= 0) return false;
        for (var i = 0; i < xy.Length; i++) xy[i] = ((xy[i].Item1 - minX) / scale, (xy[i].Item2 - minY) / scale);
        const double epsilon = 1e-12;
        double Cross(int a, int b, int c) => (xy[b].Item1 - xy[a].Item1) * (xy[c].Item2 - xy[a].Item2)
            - (xy[b].Item2 - xy[a].Item2) * (xy[c].Item1 - xy[a].Item1);
        bool OnSegment(int a, int b, int p) => Math.Abs(Cross(a, b, p)) <= epsilon
            && xy[p].Item1 >= Math.Min(xy[a].Item1, xy[b].Item1) - epsilon
            && xy[p].Item1 <= Math.Max(xy[a].Item1, xy[b].Item1) + epsilon
            && xy[p].Item2 >= Math.Min(xy[a].Item2, xy[b].Item2) - epsilon
            && xy[p].Item2 <= Math.Max(xy[a].Item2, xy[b].Item2) + epsilon;
        for (var i = 0; i < xy.Length; i++)
        {
            var next = (i + 1) % xy.Length;
            if (xy[i] == xy[next]) return false;
            for (var j = i + 1; j < xy.Length; j++)
            {
                var end = (j + 1) % xy.Length;
                if (next == j || end == i) continue;
                if ((Cross(i, next, j) * Cross(i, next, end) < 0 && Cross(j, end, i) * Cross(j, end, next) < 0)
                    || OnSegment(i, next, j) || OnSegment(i, next, end)
                    || OnSegment(j, end, i) || OnSegment(j, end, next)) return false;
            }
        }
        var area = Enumerable.Range(0, xy.Length).Sum(i => xy[i].Item1 * xy[(i + 1) % xy.Length].Item2
            - xy[(i + 1) % xy.Length].Item1 * xy[i].Item2);
        if (Math.Abs(area) <= epsilon) return false;
        var sign = Math.Sign(area);
        var remaining = Enumerable.Range(0, xy.Length).ToList();
        var output = new List<int>((xy.Length - 2) * 3);
        while (remaining.Count > 3)
        {
            var clipped = false;
            // Start at corner 1 to preserve the previous fan ordering for convex polygons.
            for (var step = 1; step <= remaining.Count; step++)
            {
                var index = step % remaining.Count;
                var a = remaining[(index + remaining.Count - 1) % remaining.Count];
                var b = remaining[index]; var c = remaining[(index + 1) % remaining.Count];
                if (sign * Cross(a, b, c) <= epsilon) continue;
                if (remaining.Any(p => p != a && p != b && p != c
                    && sign * Cross(a, b, p) >= -epsilon && sign * Cross(b, c, p) >= -epsilon
                    && sign * Cross(c, a, p) >= -epsilon)) continue;
                output.AddRange([a, b, c]); remaining.RemoveAt(index); clipped = true; break;
            }
            if (!clipped) return false;
        }
        if (sign * Cross(remaining[0], remaining[1], remaining[2]) <= epsilon) return false;
        output.AddRange(remaining); triangles = output.ToArray();
        return true;
    }
}

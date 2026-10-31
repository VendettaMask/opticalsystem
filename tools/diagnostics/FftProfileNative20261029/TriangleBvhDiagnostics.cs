using OpticalSystemDesign.Core.Backend;

// Independent hypothesis: median centroid BVH with strict node bounds. This is
// diagnostic prediction only, never a source of product geometry or hit IDs.
internal sealed class TriangleBvhDiagnostics
{
    private sealed record Node(Vector3D Minimum, Vector3D Maximum, int[] Faces, Node? Left = null, Node? Right = null);
    private readonly Node _root;
    private readonly Vector3D[][] _faces;
    internal TriangleBvhDiagnostics(IReadOnlyList<Vector3D[]> faces)
    {
        _faces = faces.ToArray();
        _root = Build(Enumerable.Range(0, faces.Count).ToArray());
    }
    private Node Build(int[] ids)
    {
        var vertices = ids.SelectMany(i => _faces[i]).ToArray();
        var min = new Vector3D(vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Min(v => v.Z));
        var max = new Vector3D(vertices.Max(v => v.X), vertices.Max(v => v.Y), vertices.Max(v => v.Z));
        if (ids.Length <= 4) return new(min, max, ids);
        var centers = ids.Select(i => (_faces[i][0] + _faces[i][1] + _faces[i][2]) / 3).ToArray();
        var spans = new[] { centers.Max(c => c.X) - centers.Min(c => c.X), centers.Max(c => c.Y) - centers.Min(c => c.Y), centers.Max(c => c.Z) - centers.Min(c => c.Z) };
        var axis = Array.IndexOf(spans, spans.Max());
        var sorted = ids.OrderBy(i => Component((_faces[i][0] + _faces[i][1] + _faces[i][2]) / 3, axis)).ThenBy(i => i).ToArray();
        var middle = sorted.Length / 2;
        return new(min, max, [], Build(sorted[..middle]), Build(sorted[middle..]));
    }
    internal int[] Candidates(Vector3D origin, Vector3D direction)
    {
        var ids = new List<int>();
        void Visit(Node node)
        {
            var near = 0d; var far = double.PositiveInfinity;
            for (var axis = 0; axis < 3; axis++)
            {
                var o = Component(origin, axis); var d = Component(direction, axis);
                var low = Component(node.Minimum, axis); var high = Component(node.Maximum, axis);
                if (d == 0) { if (o < low || o > high) return; continue; }
                var a = (low - o) / d; var b = (high - o) / d;
                near = Math.Max(near, Math.Min(a, b)); far = Math.Min(far, Math.Max(a, b));
                if (near > far) return;
            }
            if (node.Left is null) ids.AddRange(node.Faces);
            else { Visit(node.Left); Visit(node.Right!); }
        }
        Visit(_root);
        return ids.ToArray();
    }
    private static double Component(Vector3D v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
